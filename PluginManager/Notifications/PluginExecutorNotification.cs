namespace PluginManager;

/// <summary>
/// <see cref="PluginExecutor"/> の実行通知情報を表します。
/// </summary>
public sealed class PluginExecutorNotification(
    PluginExecutorNotificationType notificationType,
    string message,
    string stageId,
    int groupIndex,
    string? pluginId = null,
    string? executionId = null,
    string? skipReason = null,
    Exception? exception = null)
{
    /// <summary>通知の種別を取得します。</summary>
    public PluginExecutorNotificationType NotificationType { get; } = notificationType;

    /// <summary>通知メッセージを取得します。</summary>
    public string Message { get; } = message;

    /// <summary>対象ステージの ID を取得します。</summary>
    public string StageId { get; } = stageId;

    /// <summary>対象グループの 1 始まりインデックスを取得します。</summary>
    public int GroupIndex { get; } = groupIndex;

    /// <summary>対象プラグインの ID を取得します。グループ通知では <see langword="null"/>。</summary>
    public string? PluginId { get; } = pluginId;

    /// <summary>同一実行サイクルを識別するトレース ID を取得します。</summary>
    public string? ExecutionId { get; } = executionId;

    /// <summary>スキップ理由を取得します。スキップ以外では <see langword="null"/>。</summary>
    public string? SkipReason { get; } = skipReason;

    /// <summary>関連例外を取得します。失敗以外では <see langword="null"/>。</summary>
    public Exception? Exception { get; } = exception;
}
