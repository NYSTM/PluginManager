using System.Diagnostics;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;

namespace PluginManager;

/// <summary>
/// AssemblyLoadContext のアンロード検証を提供します。
/// </summary>
internal sealed class UnloadVerifier
{
    private readonly ILogger? _logger;

    public UnloadVerifier(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// AssemblyLoadContext がアンロードされ、GC で回収されることを検証します。
    /// </summary>
    /// <param name="context">検証対象のコンテキスト。</param>
    /// <param name="timeout">タイムアウト時間。既定値は 10 秒。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>アンロードが成功した場合は <see langword="true"/>。タイムアウトした場合は <see langword="false"/>。</returns>
    /// <remarks>
    /// <para>
    /// このメソッドは以下の手順でアンロードを検証します：
    /// </para>
    /// <list type="number">
    /// <item>WeakReference で ALC を監視</item>
    /// <item>ALC の Unload を呼び出し</item>
    /// <item>GC を複数回実行</item>
    /// <item>WeakReference.IsAlive == false になるまで待機</item>
    /// </list>
    /// </remarks>
    public async Task<bool> VerifyUnloadAsync(
        AssemblyLoadContext context,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        timeout ??= TimeSpan.FromSeconds(10);
        var contextName = context.Name ?? "(unnamed)";

        _logger?.LogDebug("アンロード検証開始: {ContextName}", contextName);

        // WeakReference で監視
        var weakRef = new WeakReference(context, trackResurrection: true);

        // Unload を呼び出し
        context.Unload();

        var stopwatch = Stopwatch.StartNew();
        var retryCount = 0;
        const int maxRetries = 10;

        while (weakRef.IsAlive && retryCount < maxRetries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (HasTimedOut(stopwatch.Elapsed, timeout.Value))
            {
                _logger?.LogWarning(
                    "アンロード検証タイムアウト: {ContextName} (試行回数: {RetryCount})",
                    contextName,
                    retryCount);

                // 診断情報を収集
                LogDiagnostics(contextName, weakRef);
                return false;
            }

            // GC を強制実行
            await Task.Run(() =>
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            }, cancellationToken);

            retryCount++;

            if (weakRef.IsAlive)
            {
                _logger?.LogDebug(
                    "ALC がまだ生存中: {ContextName} (試行回数: {RetryCount})",
                    contextName,
                    retryCount);

                var delay = TimeSpan.FromMilliseconds(100 * retryCount);
                var remaining = timeout.Value - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    _logger?.LogWarning(
                        "アンロード検証タイムアウト: {ContextName} (試行回数: {RetryCount})",
                        contextName,
                        retryCount);

                    LogDiagnostics(contextName, weakRef);
                    return false;
                }

                if (delay > remaining)
                    delay = remaining;

                await Task.Delay(delay, cancellationToken);
            }
        }

        stopwatch.Stop();
        var isUnloaded = !weakRef.IsAlive;

        if (isUnloaded)
        {
            _logger?.LogInformation(
                "アンロード検証成功: {ContextName} (試行回数: {RetryCount}, 経過時間: {ElapsedMs}ms)",
                contextName,
                retryCount,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        else
        {
            _logger?.LogError(
                "アンロード検証失敗: {ContextName} (試行回数: {RetryCount}, 経過時間: {ElapsedMs}ms)",
                contextName,
                retryCount,
                stopwatch.Elapsed.TotalMilliseconds);

            LogDiagnostics(contextName, weakRef);
        }

        return isUnloaded;
    }

    private static bool HasTimedOut(TimeSpan elapsed, TimeSpan timeout)
        => elapsed >= timeout;

    /// <summary>
    /// 診断情報をログに記録します。
    /// </summary>
    private void LogDiagnostics(string contextName, WeakReference weakRef)
    {
        if (_logger is null || !_logger.IsEnabled(LogLevel.Warning))
            return;

        _logger.LogWarning("=== アンロード診断情報 ===");
        _logger.LogWarning("Context Name: {ContextName}", contextName);
        _logger.LogWarning("WeakReference.IsAlive: {IsAlive}", weakRef.IsAlive);

        if (weakRef.Target is AssemblyLoadContext alc)
        {
            _logger.LogWarning("Assemblies in context: {Count}", alc.Assemblies.Count());

            foreach (var assembly in alc.Assemblies)
            {
                _logger.LogWarning("  - {AssemblyName}", assembly.FullName);
            }
        }

        // メモリ情報
        var gcMemory = GC.GetTotalMemory(forceFullCollection: false);
        _logger.LogWarning("GC Total Memory: {MemoryMB:F2} MB", gcMemory / 1024.0 / 1024.0);

        // GC 世代情報
        for (int gen = 0; gen <= GC.MaxGeneration; gen++)
        {
            var count = GC.CollectionCount(gen);
            _logger.LogWarning("GC Gen {Generation} Collection Count: {Count}", gen, count);
        }

        _logger.LogWarning("=== 推奨対処: ===");
        _logger.LogWarning("1. PluginLoadResult への参照を手放してください");
        _logger.LogWarning("2. プラグインが静的フィールドを使用していないか確認してください");
        _logger.LogWarning("3. イベントハンドラの購読解除を確認してください");
    }
}
