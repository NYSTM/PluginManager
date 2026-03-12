using PluginManager.Ipc;

namespace PluginManager;

/// <summary>
/// 別プロセス実行時の通知を受け取るコールバックインターフェースです。
/// </summary>
public interface IPluginProcessCallback
{
    /// <summary>
    /// 通知の生データを受け取ります。
    /// </summary>
    /// <param name="notification">通知オブジェクト。</param>
    void OnNotification(PluginProcessNotification notification) { }

    /// <summary>
    /// `PluginHost` プロセスが起動したときに呼ばれます。
    /// </summary>
    /// <param name="processId">ホストプロセス ID。</param>
    void OnHostStarted(int processId) { }

    /// <summary>
    /// プラグインのロードが完了したときに呼ばれます。
    /// </summary>
    /// <param name="pluginId">プラグイン ID。</param>
    void OnLoadCompleted(string pluginId) { }

    /// <summary>
    /// プラグインのロードに失敗したときに呼ばれます。
    /// </summary>
    /// <param name="pluginId">プラグイン ID。</param>
    /// <param name="errorMessage">エラーメッセージ。</param>
    void OnLoadFailed(string pluginId, string? errorMessage) { }

    /// <summary>
    /// プラグインの初期化が開始されたときに呼ばれます。
    /// </summary>
    /// <param name="pluginId">プラグイン ID。</param>
    void OnInitializeStarted(string pluginId) { }

    /// <summary>
    /// プラグインの初期化が完了したときに呼ばれます。
    /// </summary>
    /// <param name="pluginId">プラグイン ID。</param>
    void OnInitializeCompleted(string pluginId) { }

    /// <summary>
    /// プラグインの初期化に失敗したときに呼ばれます。
    /// </summary>
    /// <param name="pluginId">プラグイン ID。</param>
    /// <param name="errorMessage">エラーメッセージ。</param>
    void OnInitializeFailed(string pluginId, string? errorMessage) { }

    /// <summary>
    /// プラグインの実行が開始されたときに呼ばれます。
    /// </summary>
    /// <param name="pluginId">プラグイン ID。</param>
    /// <param name="stageId">ステージ ID。</param>
    void OnExecuteStarted(string pluginId, string? stageId) { }

    /// <summary>
    /// プラグインの実行が完了したときに呼ばれます。
    /// </summary>
    /// <param name="pluginId">プラグイン ID。</param>
    /// <param name="stageId">ステージ ID。</param>
    void OnExecuteCompleted(string pluginId, string? stageId) { }

    /// <summary>
    /// プラグインの実行に失敗したときに呼ばれます。
    /// </summary>
    /// <param name="pluginId">プラグイン ID。</param>
    /// <param name="stageId">ステージ ID。</param>
    /// <param name="errorMessage">エラーメッセージ。</param>
    void OnExecuteFailed(string pluginId, string? stageId, string? errorMessage) { }

    /// <summary>
    /// プラグインのアンロードが完了したときに呼ばれます。
    /// </summary>
    /// <param name="pluginId">プラグイン ID。</param>
    void OnUnloadCompleted(string pluginId) { }

    /// <summary>
    /// プラグインのアンロードに失敗したときに呼ばれます。
    /// </summary>
    /// <param name="pluginId">プラグイン ID。</param>
    /// <param name="errorMessage">エラーメッセージ。</param>
    void OnUnloadFailed(string pluginId, string? errorMessage) { }

    /// <summary>
    /// `PluginHost` がシャットダウン要求を受信したときに呼ばれます。
    /// </summary>
    void OnShutdownReceived() { }
}
