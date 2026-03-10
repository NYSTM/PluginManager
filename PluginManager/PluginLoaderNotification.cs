namespace PluginManager;

/// <summary>
/// <see cref="PluginLoader"/> のライフサイクル通知情報を表します。
/// </summary>
public sealed class PluginLoaderNotification(
    PluginLoaderNotificationType notificationType,
    string message,
    string? pluginId = null,
    string? stageId = null,
    int? attempt = null,
    string? configurationFilePath = null,
    Exception? exception = null)
{
    /// <summary>通知の種別を取得します。</summary>
    public PluginLoaderNotificationType NotificationType { get; } = notificationType;

    /// <summary>通知の説明メッセージを取得します。</summary>
    public string Message { get; } = message;

    /// <summary>対象プラグインのIDを取得します。プラグイン非依存の通知では <see langword="null"/>。</summary>
    public string? PluginId { get; } = pluginId;

    /// <summary>対象ステージのIDを取得します。ステージ非依存の通知では <see langword="null"/>。</summary>
    public string? StageId { get; } = stageId;

    /// <summary>リトライの試行回数（1 始まり）を取得します。リトライ以外の通知では <see langword="null"/>。</summary>
    public int? Attempt { get; } = attempt;

    /// <summary>使用した設定ファイルのパスを取得します。設定非依存の通知では <see langword="null"/>。</summary>
    public string? ConfigurationFilePath { get; } = configurationFilePath;

    /// <summary>通知に関連する例外を取得します。エラーがない場合は <see langword="null"/>。</summary>
    public Exception? Exception { get; } = exception;
}
