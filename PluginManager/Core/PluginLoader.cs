namespace PluginManager;

using Microsoft.Extensions.Logging;

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
/// <b>インプロセス隔離と別プロセス隔離</b><br/>
/// <see cref="PluginLoadContext"/> は同一プロセス内のアセンブリ分離を担当します。
/// 別プロセス隔離が必要なプラグインは専用ランタイムへ委譲され、現在は明示的に未対応として扱われます。
/// </para>
/// <para>
/// <b>ALC アンロードと GC について</b><br/>
/// インプロセスで読み込まれたプラグインは、<see cref="Dispose"/> または <see cref="DisposeAsync"/> を呼び出した後、
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
    private readonly PluginDiscoverer _discoverer;
    private readonly PluginLoaderNotificationPublisher _notificationPublisher;
    private readonly IReadOnlyDictionary<PluginIsolationMode, IPluginRuntime> _runtimes;
    private bool _disposed;
    private PluginConfiguration? _lastConfig;

    public PluginLoader(ILogger<PluginLoader>? logger = null)
    {
        _notificationPublisher = new(logger);
        _discoverer = new(logger);
        _runtimes = new Dictionary<PluginIsolationMode, IPluginRuntime>
        {
            [PluginIsolationMode.InProcess] = new InProcessPluginRuntime(logger),
            [PluginIsolationMode.OutOfProcess] = new OutOfProcessPluginRuntime(),
        };
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

    public IReadOnlyList<PluginDescriptor> DiscoverFromConfiguration(
        string configurationFilePath, 
        string searchPattern = "*.dll",
        CancellationToken cancellationToken = default)
        => _discoverer.DiscoverFromConfiguration(configurationFilePath, searchPattern, cancellationToken);

    public IReadOnlyList<PluginDescriptor> Discover(
        string directoryPath, 
        string searchPattern = "*.dll",
        CancellationToken cancellationToken = default)
        => _discoverer.Discover(directoryPath, searchPattern, cancellationToken);

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
        _lastConfig = config;

        if (string.IsNullOrWhiteSpace(config.PluginsPath))
            return [];

        var descriptors = _discoverer.Discover(config.PluginsPath, searchPattern);
        var groups = PluginOrderResolver.BuildExecutionGroups(descriptors, config.StageOrders, config.PluginDependencies);
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
        foreach (var runtime in _runtimes.Values)
            runtime.Unload(assemblyPath);
    }

    /// <summary>
    /// 指定したアセンブリパスのプラグインをアンロードし、GC によるメモリ回収を待機します。
    /// ALC が確実に回収されるには、呼び出し元が <see cref="PluginLoadResult"/> の参照を
    /// 手放してからこのメソッドを呼び出してください。
    /// </summary>
    /// <param name="assemblyPath">アンロードするプラグインのアセンブリパス。</param>
    /// <param name="cancellationToken">アンロード処理のキャンセル通知。</param>
    public async Task UnloadPluginAsync(string assemblyPath, CancellationToken cancellationToken = default)
    {
        foreach (var runtime in _runtimes.Values)
            await runtime.UnloadAsync(assemblyPath, cancellationToken);
    }

    /// <summary>
    /// ロード済みプラグインを指定ステージで実行します。
    /// <see cref="LoadFromConfigurationAsync"/> でロードした場合は設定の Order に従い、
    /// 同一 Order のプラグインを並列・異なる Order のプラグインを逐次実行します。
    /// <see cref="LoadAsync"/> でロードした場合はすべてのプラグインを並列実行します。
    /// </summary>
    public Task<IReadOnlyList<PluginExecutionResult>> ExecutePluginsAndWaitAsync(
        IReadOnlyList<PluginLoadResult> loadResults,
        PluginStage stage,
        PluginContext context,
        CancellationToken cancellationToken = default)
    {
        PublishNotification(PluginLoaderNotificationType.ExecuteStart, "プラグイン実行を開始します。", stageId: stage.Id);
        var task = BuildExecutionGroupsForResults(loadResults) is { Count: > 0 } groups
            ? PluginExecutor.ExecutePluginsInGroupsAsync(groups, stage, context, cancellationToken)
            : PluginExecutor.ExecutePluginsAndWaitAsync(loadResults, stage, context, cancellationToken);
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
        try
        {
            var runtime = GetRuntime(descriptor.IsolationMode);
            return await runtime.LoadAsync(descriptor, context, cancellationToken);
        }
        catch (Exception ex)
        {
            return new PluginLoadResult(descriptor, null, ex);
        }
    }

    private IPluginRuntime GetRuntime(PluginIsolationMode isolationMode)
    {
        if (_runtimes.TryGetValue(isolationMode, out var runtime))
            return runtime;

        throw new InvalidOperationException($"隔離モード '{isolationMode}' に対応するランタイムが見つかりません。");
    }

    private void UnloadAllContexts()
    {
        foreach (var runtime in _runtimes.Values)
            runtime.UnloadAll();
    }

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
            isPermanentError: r => r.Error is InvalidOperationException or NotSupportedException,
            timeoutMilliseconds: 0,
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
                    : result.Error is InvalidOperationException or NotSupportedException
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

        var result = await LoadPluginAsync(descriptor, context, cts.Token);
        if (result.Error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new PluginLoadResult(
                descriptor,
                null,
                new TimeoutException($"プラグイン '{descriptor.Name}' が {timeoutMilliseconds}ms でタイムアウトしました。"));
        }

        return result;
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

    /// <summary>
    /// キャッシュ済み設定を使用して <paramref name="loadResults"/> を Order グループに分割します。
    /// 設定がない場合は <see langword="null"/> を返します。
    /// </summary>
    private IReadOnlyList<IReadOnlyList<PluginLoadResult>>? BuildExecutionGroupsForResults(
        IReadOnlyList<PluginLoadResult> loadResults)
    {
        if (_lastConfig is null ||
            (_lastConfig.StageOrders.Count == 0 && _lastConfig.PluginDependencies.Count == 0))
            return null;

        var descriptors = loadResults.Select(r => r.Descriptor).ToList();
        var orderedGroups = PluginOrderResolver.BuildExecutionGroups(
            descriptors,
            _lastConfig.StageOrders,
            _lastConfig.PluginDependencies);

        // PluginDescriptor.Id をキーに loadResults を高速ルックアップ
        var resultById = loadResults.ToLookup(r => r.Descriptor.Id, StringComparer.OrdinalIgnoreCase);

        return orderedGroups
            .Select(group => (IReadOnlyList<PluginLoadResult>)group
                .SelectMany(d => resultById[d.Id])
                .ToList())
            .Where(g => g.Count > 0)
            .ToList();
    }

    private void PublishNotification(PluginLoaderNotificationType notificationType, string message, string? pluginId = null, string? stageId = null, int? attempt = null, string? configurationFilePath = null, Exception? exception = null)
        => _notificationPublisher.Publish(notificationType, message, pluginId, stageId, attempt, configurationFilePath, exception);
}
