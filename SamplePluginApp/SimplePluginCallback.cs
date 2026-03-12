using PluginManager;

namespace SamplePluginApp;

/// <summary>
/// コールバック方式によるプラグインロード通知の受け取り例です。
/// </summary>
public class SimplePluginCallback : IPluginLoaderCallback
{
    private readonly Action<string, LogLevel> _logAction;

    public SimplePluginCallback(Action<string, LogLevel> logAction)
    {
        _logAction = logAction;
    }

    public void OnLoadStart(string configurationFilePath)
        => _logAction($"📂 設定ファイルからロード開始: {configurationFilePath}", LogLevel.Info);

    public void OnLoadCompleted(string configurationFilePath)
        => _logAction($"✅ ロード完了", LogLevel.Success);

    public void OnPluginLoadStart(string pluginId, int attempt)
    {
        if (attempt == 1)
            _logAction($"🔄 [{pluginId}] ロード開始", LogLevel.Info);
    }

    public void OnPluginLoadRetry(string pluginId, int attempt, Exception? error)
        => _logAction($"⚠️ [{pluginId}] リトライ ({attempt} 回目): {error?.Message}", LogLevel.Warn);

    public void OnPluginLoadSuccess(string pluginId, int attempt)
    {
        var retryText = attempt > 1 ? $" ({attempt} 回目で成功)" : "";
        _logAction($"✅ [{pluginId}] ロード成功{retryText}", LogLevel.Success);
    }

    public void OnPluginLoadFailed(string pluginId, int attempt, Exception? error)
        => _logAction($"❌ [{pluginId}] ロード失敗: {error?.Message}", LogLevel.Error);

    public void OnExecuteStart(string stageId)
        => _logAction($"▶️ ステージ [{stageId}] 実行開始", LogLevel.Info);

    public void OnExecuteCompleted(string stageId)
        => _logAction($"✅ ステージ [{stageId}] 完了", LogLevel.Success);

    public void OnExecuteFailed(string stageId, Exception error)
        => _logAction($"❌ ステージ [{stageId}] エラー: {error.Message}", LogLevel.Error);
}
