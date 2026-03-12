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
    /// <param name="callback">実行通知を受け取るコールバック。</param>
    /// <param name="executionId">同一実行サイクルを識別するトレース ID。</param>
    /// <returns>
    /// 各プラグインの <see cref="PluginExecutionResult"/> リスト。
    /// 順序は <paramref name="loadResults"/> の順序に一致し、スキップされたプラグインも含まれます。
    /// </returns>
    public static async Task<IReadOnlyList<PluginExecutionResult>> ExecutePluginsAndWaitAsync(
        IReadOnlyList<PluginLoadResult> loadResults,
        PluginStage stage,
        PluginContext context,
        CancellationToken cancellationToken = default,
        IPluginExecutorCallback? callback = null,
        string? executionId = null)
    {
        var notificationPublisher = CreateNotificationPublisher(callback);
        return await ExecutePluginsAndWaitCoreAsync(loadResults, stage, context, cancellationToken, notificationPublisher, executionId);
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
    /// <param name="maxDegreeOfParallelism">同時実行上限。<see langword="null"/> の場合は無制限。</param>
    /// <param name="callback">実行通知を受け取るコールバック。</param>
    /// <param name="executionId">同一実行サイクルを識別するトレース ID。</param>
    /// <returns>
    /// 各プラグインの <see cref="PluginExecutionResult"/> リスト。
    /// グループ順・グループ内の入力順に並びます。
    /// </returns>
    public static async Task<IReadOnlyList<PluginExecutionResult>> ExecutePluginsInGroupsAsync(
        IReadOnlyList<IReadOnlyList<PluginLoadResult>> groups,
        PluginStage stage,
        PluginContext context,
        CancellationToken cancellationToken = default,
        int? maxDegreeOfParallelism = null,
        IPluginExecutorCallback? callback = null,
        string? executionId = null)
    {
        var notificationPublisher = CreateNotificationPublisher(callback);
        return await ExecutePluginsInGroupsCoreAsync(groups, stage, context, cancellationToken, maxDegreeOfParallelism, notificationPublisher, executionId);
    }

    internal static async Task<IReadOnlyList<PluginExecutionResult>> ExecutePluginsAndWaitCoreAsync(
        IReadOnlyList<PluginLoadResult> loadResults,
        PluginStage stage,
        PluginContext context,
        CancellationToken cancellationToken,
        PluginExecutorNotificationPublisher? notificationPublisher,
        string? executionId)
    {
        return await ExecuteGroupAsync(loadResults, stage, context, 1, cancellationToken, notificationPublisher, executionId);
    }

    internal static async Task<IReadOnlyList<PluginExecutionResult>> ExecutePluginsInGroupsCoreAsync(
        IReadOnlyList<IReadOnlyList<PluginLoadResult>> groups,
        PluginStage stage,
        PluginContext context,
        CancellationToken cancellationToken,
        int? maxDegreeOfParallelism,
        PluginExecutorNotificationPublisher? notificationPublisher,
        string? executionId)
    {
        var all = new List<PluginExecutionResult>();
        for (int i = 0; i < groups.Count; i++)
        {
            var groupIndex = i + 1;
            var group = groups[i];

            notificationPublisher?.Publish(
                PluginExecutorNotificationType.GroupStart,
                $"グループ {groupIndex} の実行を開始します。",
                stage.Id,
                groupIndex,
                executionId: executionId);

            var results = maxDegreeOfParallelism is > 0
                ? await ExecuteGroupWithLimitAsync(group, stage, context, groupIndex, maxDegreeOfParallelism.Value, cancellationToken, notificationPublisher, executionId)
                : await ExecuteGroupAsync(group, stage, context, groupIndex, cancellationToken, notificationPublisher, executionId);

            all.AddRange(results);

            notificationPublisher?.Publish(
                PluginExecutorNotificationType.GroupCompleted,
                $"グループ {groupIndex} の実行が完了しました。",
                stage.Id,
                groupIndex,
                executionId: executionId);
        }

        return all;
    }

    private static PluginExecutorNotificationPublisher? CreateNotificationPublisher(IPluginExecutorCallback? callback)
    {
        if (callback is null)
            return null;

        var notificationPublisher = new PluginExecutorNotificationPublisher(logger: null);
        notificationPublisher.SetCallback(callback);
        return notificationPublisher;
    }

    private static async Task<IReadOnlyList<PluginExecutionResult>> ExecuteGroupAsync(
        IReadOnlyList<PluginLoadResult> group,
        PluginStage stage,
        PluginContext context,
        int groupIndex,
        CancellationToken cancellationToken,
        PluginExecutorNotificationPublisher? notificationPublisher,
        string? executionId)
    {
        var tasks = group.Select(r => ExecuteOrSkipAsync(r, stage, context, groupIndex, cancellationToken, notificationPublisher, executionId));
        return await Task.WhenAll(tasks);
    }

    private static async Task<IReadOnlyList<PluginExecutionResult>> ExecuteGroupWithLimitAsync(
        IReadOnlyList<PluginLoadResult> group,
        PluginStage stage,
        PluginContext context,
        int groupIndex,
        int maxDegreeOfParallelism,
        CancellationToken cancellationToken,
        PluginExecutorNotificationPublisher? notificationPublisher,
        string? executionId)
    {
        using var gate = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);

        var tasks = group.Select(async r =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await ExecuteOrSkipAsync(r, stage, context, groupIndex, cancellationToken, notificationPublisher, executionId);
            }
            finally
            {
                gate.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }

    private static async Task<PluginExecutionResult> ExecuteOrSkipAsync(
        PluginLoadResult loadResult,
        PluginStage stage,
        PluginContext context,
        int groupIndex,
        CancellationToken cancellationToken,
        PluginExecutorNotificationPublisher? notificationPublisher,
        string? executionId)
    {
        if (!loadResult.Success || loadResult.Instance is null)
        {
            return CreateSkippedResult(
                loadResult.Descriptor,
                stage,
                groupIndex,
                "ロードに失敗したためスキップされました。",
                notificationPublisher,
                executionId);
        }

        if (!loadResult.Descriptor.SupportedStages.Contains(stage))
        {
            return CreateSkippedResult(
                loadResult.Descriptor,
                stage,
                groupIndex,
                $"ステージ '{stage.Id}' は対象外です。",
                notificationPublisher,
                executionId);
        }

        return await ExecuteSafeAsync(loadResult, stage, context, groupIndex, cancellationToken, notificationPublisher, executionId);
    }

    private static async Task<PluginExecutionResult> ExecuteSafeAsync(
        PluginLoadResult loadResult,
        PluginStage stage,
        PluginContext context,
        int groupIndex,
        CancellationToken cancellationToken,
        PluginExecutorNotificationPublisher? notificationPublisher,
        string? executionId)
    {
        notificationPublisher?.Publish(
            PluginExecutorNotificationType.PluginExecuteStart,
            $"プラグイン '{loadResult.Descriptor.Id}' の実行を開始します。",
            stage.Id,
            groupIndex,
            loadResult.Descriptor.Id,
            executionId);

        try
        {
            var value = await loadResult.Instance!.ExecuteAsync(stage, context, cancellationToken);
            notificationPublisher?.Publish(
                PluginExecutorNotificationType.PluginExecuteCompleted,
                $"プラグイン '{loadResult.Descriptor.Id}' の実行が完了しました。",
                stage.Id,
                groupIndex,
                loadResult.Descriptor.Id,
                executionId);
            return new(loadResult.Descriptor, value, null);
        }
        catch (Exception ex)
        {
            notificationPublisher?.Publish(
                PluginExecutorNotificationType.PluginExecuteFailed,
                $"プラグイン '{loadResult.Descriptor.Id}' の実行に失敗しました。",
                stage.Id,
                groupIndex,
                loadResult.Descriptor.Id,
                executionId,
                exception: ex);
            return new(loadResult.Descriptor, null, ex);
        }
    }

    private static PluginExecutionResult CreateSkippedResult(
        PluginDescriptor descriptor,
        PluginStage stage,
        int groupIndex,
        string skipReason,
        PluginExecutorNotificationPublisher? notificationPublisher,
        string? executionId)
    {
        notificationPublisher?.Publish(
            PluginExecutorNotificationType.PluginSkipped,
            $"プラグイン '{descriptor.Id}' はスキップされました。理由: {skipReason}",
            stage.Id,
            groupIndex,
            descriptor.Id,
            executionId,
            skipReason: skipReason);

        return PluginExecutionResult.CreateSkipped(descriptor, skipReason);
    }
}
