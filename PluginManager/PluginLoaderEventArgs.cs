namespace PluginManager;

/// <summary>
/// <see cref="PluginLoader"/> が発行するライフサイクルイベントの種別です。
/// </summary>
public enum PluginLoaderEventType
{
    /// <summary>設定ファイルを使用したロード処理の開始。</summary>
    LoadStart,
    /// <summary>設定ファイルを使用したロード処理の完了。</summary>
    LoadCompleted,
    /// <summary>個別プラグインのロード開始。</summary>
    PluginLoadStart,
    /// <summary>個別プラグインのロードリトライ。</summary>
    PluginLoadRetry,
    /// <summary>個別プラグインのロード成功。</summary>
    PluginLoadSuccess,
    /// <summary>個別プラグインのロード失敗（リトライ上限到達・恒久的エラー・キャンセルを含む）。</summary>
    PluginLoadFailed,
    /// <summary>ステージ実行の開始。</summary>
    ExecuteStart,
    /// <summary>ステージ実行の完了。</summary>
    ExecuteCompleted,
    /// <summary>ステージ実行中のエラー発生。</summary>
    ExecuteFailed,
}

/// <summary>
/// <see cref="PluginLoader"/> のライフサイクル通知情報を表します。
/// </summary>
public sealed class PluginLoaderEventArgs(
    PluginLoaderEventType eventType,
    string message,
    string? pluginId = null,
    string? stageId = null,
    int? attempt = null,
    string? configurationFilePath = null,
    Exception? exception = null) : EventArgs
{
    /// <summary>イベントの種別を取得します。</summary>
    public PluginLoaderEventType EventType { get; } = eventType;

    /// <summary>イベントの説明メッセージを取得します。</summary>
    public string Message { get; } = message;

    /// <summary>対象プラグインのIDを取得します。プラグイン非依存のイベントでは <see langword="null"/>。</summary>
    public string? PluginId { get; } = pluginId;

    /// <summary>対象ステージのIDを取得します。ステージ非依存のイベントでは <see langword="null"/>。</summary>
    public string? StageId { get; } = stageId;

    /// <summary>リトライの試行回数（1 始まり）を取得します。リトライ以外のイベントでは <see langword="null"/>。</summary>
    public int? Attempt { get; } = attempt;

    /// <summary>使用した設定ファイルのパスを取得します。設定非依存のイベントでは <see langword="null"/>。</summary>
    public string? ConfigurationFilePath { get; } = configurationFilePath;

    /// <summary>イベントに関連する例外を取得します。エラーがない場合は <see langword="null"/>。</summary>
    public Exception? Exception { get; } = exception;
}
