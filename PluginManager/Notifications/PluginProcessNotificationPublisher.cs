using Microsoft.Extensions.Logging;
using PluginManager.Ipc;

namespace PluginManager;

/// <summary>
/// 別プロセス通知のコールバック通知とログ出力を担います。
/// </summary>
internal sealed class PluginProcessNotificationPublisher(ILogger<PluginLoader>? logger)
{
    private readonly ILogger<PluginLoader>? _logger = logger;
    private IPluginProcessCallback? _callback;

    public void SetCallback(IPluginProcessCallback? callback)
        => _callback = callback;

    public void Publish(PluginProcessNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        InvokeCallback(notification);

        if (_logger is null)
            return;

        if (!string.IsNullOrWhiteSpace(notification.ErrorMessage))
        {
            _logger.LogError(
                "{Message} ProcessNotificationType={NotificationType}, PluginId={PluginId}, StageId={StageId}, ProcessId={ProcessId}, ErrorType={ErrorType}",
                notification.Message,
                notification.NotificationType,
                notification.PluginId,
                notification.StageId,
                notification.ProcessId,
                notification.ErrorType);
            return;
        }

        _logger.LogInformation(
            "{Message} ProcessNotificationType={NotificationType}, PluginId={PluginId}, StageId={StageId}, ProcessId={ProcessId}",
            notification.Message,
            notification.NotificationType,
            notification.PluginId,
            notification.StageId,
            notification.ProcessId);
    }

    private void InvokeCallback(PluginProcessNotification notification)
    {
        if (_callback is null)
            return;

        try
        {
            _callback.OnNotification(notification);

            switch (notification.NotificationType)
            {
                case PluginProcessNotificationType.HostStarted:
                    _callback.OnHostStarted(notification.ProcessId);
                    break;
                case PluginProcessNotificationType.LoadCompleted:
                    if (notification.PluginId is not null)
                        _callback.OnLoadCompleted(notification.PluginId);
                    break;
                case PluginProcessNotificationType.LoadFailed:
                    if (notification.PluginId is not null)
                        _callback.OnLoadFailed(notification.PluginId, notification.ErrorMessage);
                    break;
                case PluginProcessNotificationType.InitializeStarted:
                    if (notification.PluginId is not null)
                        _callback.OnInitializeStarted(notification.PluginId);
                    break;
                case PluginProcessNotificationType.InitializeCompleted:
                    if (notification.PluginId is not null)
                        _callback.OnInitializeCompleted(notification.PluginId);
                    break;
                case PluginProcessNotificationType.InitializeFailed:
                    if (notification.PluginId is not null)
                        _callback.OnInitializeFailed(notification.PluginId, notification.ErrorMessage);
                    break;
                case PluginProcessNotificationType.ExecuteStarted:
                    if (notification.PluginId is not null)
                        _callback.OnExecuteStarted(notification.PluginId, notification.StageId);
                    break;
                case PluginProcessNotificationType.ExecuteCompleted:
                    if (notification.PluginId is not null)
                        _callback.OnExecuteCompleted(notification.PluginId, notification.StageId);
                    break;
                case PluginProcessNotificationType.ExecuteFailed:
                    if (notification.PluginId is not null)
                        _callback.OnExecuteFailed(notification.PluginId, notification.StageId, notification.ErrorMessage);
                    break;
                case PluginProcessNotificationType.UnloadCompleted:
                    if (notification.PluginId is not null)
                        _callback.OnUnloadCompleted(notification.PluginId);
                    break;
                case PluginProcessNotificationType.UnloadFailed:
                    if (notification.PluginId is not null)
                        _callback.OnUnloadFailed(notification.PluginId, notification.ErrorMessage);
                    break;
                case PluginProcessNotificationType.ShutdownReceived:
                    _callback.OnShutdownReceived();
                    break;
            }
        }
        catch
        {
            // コールバック内の例外は無視（通知処理が本体の処理を妨げないようにする）
        }
    }
}
