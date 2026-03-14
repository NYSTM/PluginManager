using PluginManager.Ipc;

namespace PluginHost;

/// <summary>
/// `PluginHost` 内部イベントを別プロセス通知へ変換します。
/// </summary>
internal sealed class PluginHostNotifier
{
    private readonly MemoryMappedNotificationQueue? _notificationQueue;

    public PluginHostNotifier(MemoryMappedNotificationQueue? notificationQueue = null)
    {
        _notificationQueue = notificationQueue;
    }

    public void Notify(
        PluginProcessNotificationType notificationType,
        string message,
        string? requestId = null,
        string? pluginId = null,
        string? stageId = null,
        string? errorType = null,
        string? errorMessage = null)
    {
        _notificationQueue?.Enqueue(new PluginProcessNotification
        {
            NotificationType = notificationType,
            Message = message,
            RequestId = requestId,
            PluginId = pluginId,
            StageId = stageId,
            ProcessId = Environment.ProcessId,
            ErrorType = errorType,
            ErrorMessage = errorMessage,
        });
    }
}
