namespace PluginManager;

using Microsoft.Extensions.Logging;
using PluginManager;

/// <summary>
/// プラグインのロード・実行・アンロードを管理するメインクラスです。
/// </summary>
/// <remarks>
/// <para>
/// <b>廃棄パターン</b><br/>
/// <see cref="Dispose"/> は GC を Fire-and-forget で実行するため UI スレッドをブロックしません。<br/>
/// GC 完了まで待機したい場合（再ロード前など）は <see cref="DisposeAsync"/> を使用してください。
/// </para>
/// <para>
/// <b>ALC アンロードと GC について</b><br/>
/// <see cref="Dispose"/> または <see cref="DisposeAsync"/> を呼び出した後、
/// ALC（AssemblyLoadContext）が実際にメモリから回収されるには、
/// 呼び出し元が <see cref="PluginLoadResult"/> のリストへの参照を手放す必要があります。
/// <see cref="IPlugin"/> インスタンスへの強参照が残っている限り、ALC は GC されません。
/// </para>
/// <para>
/// <b>通知方式</b><br/>
/// ライフサイクル通知を受け取るには <see cref="SetCallback"/> でコールバックを設定してください。
/// </para>
/// </remarks>
public sealed class PluginLoader : IDisposable, IAsyncDisposable
{
    private readonly Dictionary<string, PluginLoadContext> _loadContexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly PluginDiscoverer _discoverer;
    private readonly PluginLoaderNotificationPublisher _notificationPublisher;
    private bool _disposed;

    public PluginLoader(ILogger<PluginLoader>? logger = null)
    {
        _notificationPublisher = new(logger);
        _discoverer = new(logger);
    }

    /// <summary>
    /// ライフサイクル通知を受け取るコールバックを設定します。
    /// </summary>
    /// <param name="callback">通知を受け取るコールバック実装。<see langword="null"/> で解除。</param>
    /// <example>
    /// <code>
    /// public class MyCallback : IPluginLoaderCallback
    /// {
    ///     public void OnPluginLoadSuccess(string pluginId, int attempt)
    ///         => Console.WriteLine($"[OK] {pluginId}");
    /// }
    /// 
    /// var loader = new PluginLoader();
    /// loader.SetCallback(new MyCallback());
    /// await loader.LoadFromConfigurationAsync(...);
    /// </code>
    /// </example>
    public void SetCallback(IPluginLoaderCallback? callback)
        => _notificationPublisher.SetCallback(callback);

    public IReadOnlyList<PluginDescriptor> DiscoverFromConfiguration(string configurationFilePath, string searchPattern = "*.dll")
        => _discoverer.DiscoverFromConfiguration(configurationFilePath, searchPattern);

    public IReadOnlyList<PluginDescriptor> Discover(string directoryPath, string searchPattern = "*.dll")
        => _discoverer.Discover(directoryPath, searchPattern);

    public async Task<IReadOnlyList<PluginLoadResult>> LoadFromConfigurationAsync(
        string configurationFilePath,
        PluginContext context,
        string searchPattern = "*.dll",
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PublishNotification(PluginLoaderNotificationType.LoadStart, "設定ファイルを使用したプラグインロードを開始します。",
            configurationFilePath: configurationFilePath);

        var config = PluginConfigurationLoader.Load(configurationFilePath);
        if (string.IsNullOrWhiteSpace(config.PluginsPath))
            return [];

        var descriptors = _discoverer.Discover(config.PluginsPath, searchPattern);
        var groups = PluginOrderResolver.BuildExecutionGroups(descriptors, config.StageOrders);
        var results = new List<PluginLoadResult>(descriptors.Count);

        foreach (var group in groups)
        {
            var tasks = group.Select(d => LoadPluginWithIntervalAsync(
                d,
                context,
                config.IntervalMilliseconds,
                config.TimeoutMilliseconds,
                config.RetryCount,
                config.RetryDelayMilliseconds,
                cancellationToken));

            results.AddRange(await Task.WhenAll(tasks));
        }

        PublishNotification(PluginLoaderNotificationType.LoadCompleted, "設定ファイルを使用したプラグインロードが完了しました。",
            configurationFilePath: configurationFilePath);

        return results;
    }

    public async Task<IReadOnlyList<PluginLoadResult>> LoadAsync(
        string directoryPath,
        PluginContext context,
        string searchPattern = "*.dll",
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var descriptors = _discoverer.Discover(directoryPath, searchPattern);
        var tasks = descriptors.Select(d => LoadPluginAsync(d, context, cancellationToken));
        return await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 指定したアセンブリパスのプラグインをアンロードします。
    /// ALC が GC されるまでには、呼び出し元が <see cref="PluginLoadResult"/> の参照を
    /// 手放す必要があります。完了を待機する場合は <see cref="UnloadPluginAsync"/> を使用してください。
    /// </summary>
    /// <param name="assemblyPath">アンロードするプラグインのアセンブリパス。</param>
    public void UnloadPlugin(string assemblyPath)
    {
        var ctx = RemoveContext(assemblyPath);
        if (ctx is null)
            return;

        ctx.Unload();
        // Fire-and-forget: 呼び出し元が参照を解放次第 GC が ALC を回収する
        // 完了を保証したい場合は UnloadPluginAsync を使用すること
        _ = Task.Run(ForceCollect);
    }

    /// <summary>
    /// 指定したアセンブリパスのプラグインをアンロードし、GC によるメモリ回収を待機します。
    /// ALC が確実に回収されるには、呼び出し元が <see cref="PluginLoadResult"/> の参照を
    /// 手放してからこのメソッドを呼び出してください。
    /// </summary>
    /// <param name="assemblyPath">アンロードするプラグインのアセンブリパス。</param>
    public async Task UnloadPluginAsync(string assemblyPath)
    {
        var ctx = RemoveContext(assemblyPath);
        if (ctx is null)
            return;

        ctx.Unload();
        await Task.Run(ForceCollect);
    }

    public Task<IReadOnlyList<PluginExecutionResult>> ExecutePluginsAndWaitAsync(
        IReadOnlyList<PluginLoadResult> loadResults,
        PluginStage stage,
        PluginContext context,
        CancellationToken cancellationToken = default)
    {
        PublishNotification(PluginLoaderNotificationType.ExecuteStart, "プラグイン実行を開始します。", stageId: stage.Id);
        var task = PluginExecutor.ExecutePluginsAndWaitAsync(loadResults, stage, context, cancellationToken);
        return CompleteExecuteAsync(task, stage.Id);
    }

    /// <summary>
    /// すべてのプラグインコンテキストをアンロードしてリソースを解放します。
    /// ALC が確実に GC されるには、呼び出し元が保持する <see cref="PluginLoadResult"/> への
    /// 参照も null クリアしてください。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        UnloadAllContexts();
        _ = Task.Run(ForceCollect);
    }

    /// <summary>
    /// すべての ALC をアンロードし、GC によるメモリ回収を待機します。
    /// WinForms / WPF など UI スレッドから呼ぶ場合は必ず <c>await</c> してください。
    /// </summary>
    /// <example>
    /// <code>
    /// // WinForms の FormClosed イベント
    /// private async void Form1_FormClosed(object? sender, FormClosedEventArgs e)
    ///     => await (_loader?.DisposeAsync() ?? ValueTask.CompletedTask);
    /// </code>
    /// </example>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        UnloadAllContexts();
        await Task.Run(ForceCollect);
    }

    private async Task<PluginLoadResult> LoadPluginAsync(PluginDescriptor descriptor, PluginContext context, CancellationToken cancellationToken)
    {
        PluginLoadContext? loadContext = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            loadContext = new PluginLoadContext(descriptor.AssemblyPath);

            lock (_lock)
            {
                if (_loadContexts.TryGetValue(descriptor.AssemblyPath, out var old))
                    old.Unload();
                _loadContexts[descriptor.AssemblyPath] = loadContext;
            }

            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(descriptor.AssemblyPath));
            var type = assembly.GetType(descriptor.PluginType.FullName ?? descriptor.PluginType.Name);
            if (type is null || Activator.CreateInstance(type) is not IPlugin plugin)
            {
                RemoveContext(descriptor.AssemblyPath, loadContext);
                return new PluginLoadResult(
                    descriptor,
                    null,
                    new InvalidOperationException($"型 '{descriptor.PluginType.FullName}' は有効なプラグインではありません。"));
            }

            cancellationToken.ThrowIfCancellationRequested();
            await plugin.InitializeAsync(context, cancellationToken);
            return new PluginLoadResult(descriptor, plugin, null);
        }
        catch (Exception ex)
        {
            RemoveContext(descriptor.AssemblyPath, loadContext);
            return new PluginLoadResult(descriptor, null, ex);
        }
    }

    private void RemoveContext(string assemblyPath, PluginLoadContext? loadContext)
    {
        lock (_lock)
        {
            if (loadContext is not null &&
                _loadContexts.TryGetValue(assemblyPath, out var current) &&
                ReferenceEquals(current, loadContext))
            {
                _loadContexts.Remove(assemblyPath);
            }
        }

        loadContext?.Unload();
    }

    /// <summary>
    /// 指定パスの <see cref="PluginLoadContext"/> を辞書から取り出します。
    /// </summary>
    private PluginLoadContext? RemoveContext(string assemblyPath)
    {
        lock (_lock)
        {
            if (_loadContexts.TryGetValue(assemblyPath, out var ctx))
            {
                _loadContexts.Remove(assemblyPath);
                return ctx;
            }
            return null;
        }
    }

    /// <summary>
    /// すべての <see cref="PluginLoadContext"/> を辞書から取り出して Unload します。
    /// </summary>
    private void UnloadAllContexts()
    {
        List<PluginLoadContext> contexts;
        lock (_lock)
        {
            contexts = [.. _loadContexts.Values];
            _loadContexts.Clear();
        }

        foreach (var ctx in contexts)
            ctx.Unload();
    }

    /// <summary>
    /// GC を強制的に 2 サイクル実行して ALC の回収を促します。
    /// 必ず UI スレッド以外（Task.Run 経由）から呼び出してください。
    /// </summary>
    private static void ForceCollect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private async Task<PluginLoadResult> LoadPluginWithIntervalAsync(
        PluginDescriptor descriptor,
        PluginContext context,
        int intervalMilliseconds,
        int timeoutMilliseconds,
        int retryCount,
        int retryDelayMilliseconds,
        CancellationToken cancellationToken)
    {
        var result = await LoadPluginWithRetryAsync(
            descriptor, context, timeoutMilliseconds, retryCount, retryDelayMilliseconds, cancellationToken);

        if (intervalMilliseconds > 0 && !cancellationToken.IsCancellationRequested)
        {
            try { await Task.Delay(intervalMilliseconds, cancellationToken); }
            catch (OperationCanceledException) { }
        }

        return result;
    }

    private async Task<PluginLoadResult> LoadPluginWithRetryAsync(
        PluginDescriptor descriptor,
        PluginContext context,
        int timeoutMilliseconds,
        int retryCount,
        int retryDelayMilliseconds,
        CancellationToken cancellationToken)
    {
        return await RetryHelper.ExecuteWithRetryAsync(
            operation: async ct => await LoadPluginWithTimeoutAsync(descriptor, context, timeoutMilliseconds, ct),
            isSuccess: r => r.Success,
            isPermanentError: r => r.Error is InvalidOperationException,
            timeoutMilliseconds: 0, // タイムアウトは LoadPluginWithTimeoutAsync 内で処理済み
            retryCount: retryCount,
            retryDelayMilliseconds: retryDelayMilliseconds,
            cancellationToken: cancellationToken,
            onStart: attempt =>
            {
                PublishNotification(
                    PluginLoaderNotificationType.PluginLoadStart,
                    $"プラグイン '{descriptor.Id}' のロードを開始します。",
                    pluginId: descriptor.Id,
                    attempt: attempt);
            },
            onSuccess: (attempt, _) =>
            {
                PublishNotification(
                    PluginLoaderNotificationType.PluginLoadSuccess,
                    $"プラグイン '{descriptor.Id}' のロードに成功しました。",
                    pluginId: descriptor.Id,
                    attempt: attempt);
            },
            onRetry: (attempt, result) =>
            {
                PublishNotification(
                    PluginLoaderNotificationType.PluginLoadRetry,
                    $"プラグイン '{descriptor.Id}' のロードをリトライします。",
                    pluginId: descriptor.Id,
                    attempt: attempt,
                    exception: result.Error);
            },
            onFailed: (attempt, result) =>
            {
                var reason = cancellationToken.IsCancellationRequested
                    ? "キャンセルによりプラグインロードを中断しました。"
                    : result.Error is InvalidOperationException
                        ? "恒久的エラーによりプラグインロードに失敗しました。"
                        : "リトライ上限に到達しプラグインロードに失敗しました。";

                PublishNotification(
                    PluginLoaderNotificationType.PluginLoadFailed,
                    $"プラグイン '{descriptor.Id}' のロードに失敗しました。理由: {reason}",
                    pluginId: descriptor.Id,
                    attempt: attempt,
                    exception: result.Error);
            });
    }

    private async Task<PluginLoadResult> LoadPluginWithTimeoutAsync(
        PluginDescriptor descriptor,
        PluginContext context,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        if (timeoutMilliseconds <= 0)
            return await LoadPluginAsync(descriptor, context, cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMilliseconds);

        try
        {
            return await LoadPluginAsync(descriptor, context, cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PluginLoadResult(
                descriptor,
                null,
                new TimeoutException($"プラグイン '{descriptor.Name}' が {timeoutMilliseconds}ms でタイムアウトしました。"));
        }
    }

    private async Task<IReadOnlyList<PluginExecutionResult>> CompleteExecuteAsync(Task<IReadOnlyList<PluginExecutionResult>> executeTask, string stageId)
    {
        try
        {
            var result = await executeTask;
            PublishNotification(PluginLoaderNotificationType.ExecuteCompleted, "プラグイン実行が完了しました。", stageId: stageId);
            return result;
        }
        catch (Exception ex)
        {
            PublishNotification(PluginLoaderNotificationType.ExecuteFailed, "プラグイン実行中にエラーが発生しました。", stageId: stageId, exception: ex);
            throw;
        }
    }

    private void PublishNotification(PluginLoaderNotificationType notificationType, string message, string? pluginId = null, string? stageId = null, int? attempt = null, string? configurationFilePath = null, Exception? exception = null)
        => _notificationPublisher.Publish(notificationType, message, pluginId, stageId, attempt, configurationFilePath, exception);
}
