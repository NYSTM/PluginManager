namespace PluginManager;

/// <summary>
/// <see cref="PluginExecutor"/> の実行通知を受け取るコールバックインターフェースです。
/// </summary>
public interface IPluginExecutorCallback
{
    /// <summary>
    /// 通知の生データを受け取ります。
    /// </summary>
    /// <param name="notification">通知オブジェクト。</param>
    void OnNotification(PluginExecutorNotification notification) { }

    /// <summary>
    /// 実行グループが開始されたときに呼ばれます。
    /// </summary>
    /// <param name="stageId">ステージの ID。</param>
    /// <param name="groupIndex">グループの 1 始まりインデックス。</param>
    void OnGroupStart(string stageId, int groupIndex) { }

    /// <summary>
    /// 実行グループが完了したときに呼ばれます。
    /// </summary>
    /// <param name="stageId">ステージの ID。</param>
    /// <param name="groupIndex">グループの 1 始まりインデックス。</param>
    void OnGroupCompleted(string stageId, int groupIndex) { }

    /// <summary>
    /// 個別プラグイン実行が開始されたときに呼ばれます。
    /// </summary>
    /// <param name="stageId">ステージの ID。</param>
    /// <param name="pluginId">プラグインの ID。</param>
    /// <param name="groupIndex">グループの 1 始まりインデックス。</param>
    void OnPluginExecuteStart(string stageId, string pluginId, int groupIndex) { }

    /// <summary>
    /// 個別プラグイン実行が成功したときに呼ばれます。
    /// </summary>
    /// <param name="stageId">ステージの ID。</param>
    /// <param name="pluginId">プラグインの ID。</param>
    /// <param name="groupIndex">グループの 1 始まりインデックス。</param>
    void OnPluginExecuteCompleted(string stageId, string pluginId, int groupIndex) { }

    /// <summary>
    /// 個別プラグイン実行が失敗したときに呼ばれます。
    /// </summary>
    /// <param name="stageId">ステージの ID。</param>
    /// <param name="pluginId">プラグインの ID。</param>
    /// <param name="groupIndex">グループの 1 始まりインデックス。</param>
    /// <param name="error">エラー内容。</param>
    void OnPluginExecuteFailed(string stageId, string pluginId, int groupIndex, Exception error) { }

    /// <summary>
    /// 個別プラグインがスキップされたときに呼ばれます。
    /// </summary>
    /// <param name="stageId">ステージの ID。</param>
    /// <param name="pluginId">プラグインの ID。</param>
    /// <param name="groupIndex">グループの 1 始まりインデックス。</param>
    /// <param name="skipReason">スキップ理由。</param>
    void OnPluginSkipped(string stageId, string pluginId, int groupIndex, string skipReason) { }
}
