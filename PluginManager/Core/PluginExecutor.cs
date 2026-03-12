using PluginManager;

/// <summary>
/// ロード済みプラグインをステージに応じて実行します。
/// </summary>
public static class PluginExecutor
{
    /// <summary>
    /// ロード済みプラグインを指定ステージで並行実行し、全タスクの完了を待機して結果を返します。
    /// 個々のプラグインが例外をスローした場合も他のプラグイン結果は保持されます。
    /// SupportedStages が一致しないプラグインやロードに失敗したプラグインはスキップされます。
    /// </summary>
    /// <param name="loadResults">ロード結果の一覧。</param>
    /// <param name="stage">実行するライフサイクルステージ。</param>
    /// <param name="context">実行コンテキスト。プラグイン間でデータを共有します。</param>
    /// <param name="cancellationToken">キャンセル通知。</param>
    /// <returns>
    /// 各プラグインの <see cref="PluginExecutionResult"/> リスト。
    /// 順序は <paramref name="loadResults"/> の順序に一致し、スキップされたプラグインも含まれます。
    /// </returns>
    public static async Task<IReadOnlyList<PluginExecutionResult>> ExecutePluginsAndWaitAsync(
        IReadOnlyList<PluginLoadResult> loadResults,
        PluginStage stage,
        PluginContext context,
        CancellationToken cancellationToken = default)
    {
        var tasks = loadResults.Select(r => ExecuteOrSkipAsync(r, stage, context, cancellationToken));
        return await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Order グループ化された <see cref="PluginLoadResult"/> を順次・並列で実行します。
    /// 同一グループ内のプラグインは並列実行し、グループ間は前グループの完了後に次グループを開始します。
    /// </summary>
    /// <param name="groups">
    /// Order 昇順に並んだロード結果のグループ一覧。
    /// 同一 Order のプラグインを同一グループにまとめてください。
    /// </param>
    /// <param name="stage">実行するライフサイクルステージ。</param>
    /// <param name="context">実行コンテキスト。プラグイン間でデータを共有します。</param>
    /// <param name="cancellationToken">キャンセル通知。</param>
    /// <returns>
    /// 各プラグインの <see cref="PluginExecutionResult"/> リスト。
    /// グループ順・グループ内の入力順に並びます。
    /// </returns>
    public static async Task<IReadOnlyList<PluginExecutionResult>> ExecutePluginsInGroupsAsync(
        IReadOnlyList<IReadOnlyList<PluginLoadResult>> groups,
        PluginStage stage,
        PluginContext context,
        CancellationToken cancellationToken = default)
    {
        var all = new List<PluginExecutionResult>();
        foreach (var group in groups)
        {
            var tasks = group.Select(r => ExecuteOrSkipAsync(r, stage, context, cancellationToken));
            var results = await Task.WhenAll(tasks);
            all.AddRange(results);
        }
        return all;
    }

    /// <summary>
    /// プラグインを実行するか、条件に応じてスキップします。
    /// </summary>
    private static async Task<PluginExecutionResult> ExecuteOrSkipAsync(
        PluginLoadResult loadResult,
        PluginStage stage,
        PluginContext context,
        CancellationToken cancellationToken)
    {
        // ロード失敗したプラグインはスキップ
        if (!loadResult.Success || loadResult.Instance is null)
        {
            return PluginExecutionResult.CreateSkipped(
                loadResult.Descriptor,
                "ロードに失敗したためスキップされました。");
        }

        // SupportedStages に含まれないプラグインはスキップ
        // 注: Descriptor の SupportedStages を使用（Instance ではなく）
        if (!loadResult.Descriptor.SupportedStages.Contains(stage))
        {
            return PluginExecutionResult.CreateSkipped(
                loadResult.Descriptor,
                $"ステージ '{stage.Id}' は対象外です。");
        }

        // 実行
        return await ExecuteSafeAsync(loadResult, stage, context, cancellationToken);
    }

    /// <summary>
    /// プラグインを安全に実行します。例外が発生した場合は結果に含めます。
    /// </summary>
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
