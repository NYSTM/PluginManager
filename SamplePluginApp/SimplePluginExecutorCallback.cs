using PluginManager;

namespace SamplePluginApp;

/// <summary>
/// コールバック方式によるプラグイン実行通知の受け取り例です。
/// </summary>
public class SimplePluginExecutorCallback : IPluginExecutorCallback
{
    private readonly Action<string, LogLevel> _logAction;

    public SimplePluginExecutorCallback(Action<string, LogLevel> logAction)
    {
        _logAction = logAction;
    }

    public void OnGroupStart(string stageId, int groupIndex)
        => _logAction($"📦 [{stageId}] グループ {groupIndex} 開始", LogLevel.Info);

    public void OnGroupCompleted(string stageId, int groupIndex)
        => _logAction($"✅ [{stageId}] グループ {groupIndex} 完了", LogLevel.Success);

    public void OnPluginExecuteStart(string stageId, string pluginId, int groupIndex)
        => _logAction($"▶️ [{stageId}] Group={groupIndex} Plugin={pluginId} 実行開始", LogLevel.Info);

    public void OnPluginExecuteCompleted(string stageId, string pluginId, int groupIndex)
        => _logAction($"✅ [{stageId}] Group={groupIndex} Plugin={pluginId} 実行完了", LogLevel.Success);

    public void OnPluginExecuteFailed(string stageId, string pluginId, int groupIndex, Exception error)
        => _logAction($"❌ [{stageId}] Group={groupIndex} Plugin={pluginId} 実行失敗: {error.Message}", LogLevel.Error);

    public void OnPluginSkipped(string stageId, string pluginId, int groupIndex, string skipReason)
        => _logAction($"⏭️ [{stageId}] Group={groupIndex} Plugin={pluginId} スキップ: {skipReason}", LogLevel.Warn);
}
