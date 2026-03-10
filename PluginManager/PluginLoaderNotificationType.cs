namespace PluginManager;

/// <summary>
/// <see cref="PluginLoader"/> が発行するライフサイクル通知の種別です。
/// </summary>
public enum PluginLoaderNotificationType
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
