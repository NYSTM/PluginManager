namespace PluginManager.Ipc;

/// <summary>
/// 別プロセスプラグイン実行時の通知種別を表します。
/// </summary>
public enum PluginProcessNotificationType
{
    HostStarted,
    HostShutdownRequested,
    HostFatalError,
    PipeServerStarted,
    PipeServerStopped,
    ClientConnectionWaiting,
    ClientConnected,
    ConnectionProcessingFailed,
    RequestProcessingFailed,
    ServerInstanceFailed,
    UnloadAllStarted,
    UnloadAllCompleted,
    LoadCompleted,
    LoadFailed,
    InitializeStarted,
    InitializeCompleted,
    InitializeFailed,
    ExecuteStarted,
    ExecuteCompleted,
    ExecuteFailed,
    UnloadCompleted,
    UnloadFailed,
    ShutdownReceived,
}
