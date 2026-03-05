using PluginManager;

/// <summary>
/// ロード済みプラグインをステージに応じて実行します。
/// </summary>
public static class PluginExecutor
{
    /// <summary>
    /// ロード済みプラグインを指定ステージで並行実行し、全タスクの完了を待機して結果を返します。
    /// 個々のプラグインが例外をスローした場合も他のプラグイン結果は保持されます。
    /// SupportedStages が一致するプラグインのみが実行対象になります。
    /// </summary>
    /// <param name="loadResults">ロード結果の一覧。失敗したプラグインは無視されます。</param>
    /// <param name="stage">実行するライフサイクルステージ。</param>
    /// <param name="context">実行コンテキスト。プラグイン間でデータを共有します。</param>
    /// <param name="cancellationToken">キャンセル通知。</param>
    /// <returns>各プラグインの <see cref="PluginExecutionResult"/> リスト（順序は loadResults の順序に一致）。</returns>
    public static async Task<IReadOnlyList<PluginExecutionResult>> ExecutePluginsAndWaitAsync(
        IReadOnlyList<PluginLoadResult> loadResults,
        PluginStage stage,
        PluginContext context,
        CancellationToken cancellationToken = default)
    {
        var targets = loadResults
            .Where(r => r.Success && r.Instance is not null && r.Instance.SupportedStages.Contains(stage))
            .ToList();

        var tasks = targets.Select(r => ExecuteSafeAsync(r, stage, context, cancellationToken));
        return await Task.WhenAll(tasks);
    }

    private static async Task<PluginExecutionResult> ExecuteSafeAsync(
        PluginLoadResult loadResult,
        PluginStage stage,
        PluginContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await loadResult.Instance!.ExecuteAsync(stage, context, cancellationToken);
            return new(loadResult.Descriptor, value, null);
        }
        catch (Exception ex)
        {
            return new(loadResult.Descriptor, null, ex);
        }
    }
}
