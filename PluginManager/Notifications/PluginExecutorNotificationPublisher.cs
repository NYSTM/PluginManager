using Microsoft.Extensions.Logging;

namespace PluginManager;

/// <summary>
/// <see cref="PluginExecutor"/> のコールバック通知とログ出力を担います。
/// </summary>
internal sealed class PluginExecutorNotificationPublisher(ILogger<PluginLoader>? logger)
{
    private readonly ILogger<PluginLoader>? _logger = logger;
    private IPluginExecutorCallback? _callback;

    /// <summary>
    /// コールバックを設定します。
    /// </summary>
    public void SetCallback(IPluginExecutorCallback? callback)
        => _callback = callback;

    public void Publish(
        PluginExecutorNotificationType notificationType,
        string message,
        string stageId,
        int groupIndex,
        string? pluginId = null,
        string? executionId = null,
        string? skipReason = null,
        Exception? exception = null)
    {
        var notification = new PluginExecutorNotification(
            notificationType,
            message,
            stageId,
            groupIndex,
            pluginId,
            executionId,
            skipReason,
            exception);

        InvokeCallback(notification);

        if (_logger is null)
            return;

        if (exception is not null)
        {
            _logger.LogError(exception,
                "{Message} NotificationType={NotificationType}, StageId={StageId}, GroupIndex={GroupIndex}, PluginId={PluginId}, ExecutionId={ExecutionId}, SkipReason={SkipReason}",
                message, notificationType, stageId, groupIndex, pluginId, executionId, skipReason);
            return;
        }

        _logger.LogInformation(
            "{Message} NotificationType={NotificationType}, StageId={StageId}, GroupIndex={GroupIndex}, PluginId={PluginId}, ExecutionId={ExecutionId}, SkipReason={SkipReason}",
            message, notificationType, stageId, groupIndex, pluginId, executionId, skipReason);
    }

    private void InvokeCallback(PluginExecutorNotification notification)
    {
        if (_callback is null)
            return;

        try
        {
            _callback.OnNotification(notification);

            switch (notification.NotificationType)
            {
                case PluginExecutorNotificationType.GroupStart:
                    _callback.OnGroupStart(notification.StageId, notification.GroupIndex);
                    break;

                case PluginExecutorNotificationType.GroupCompleted:
                    _callback.OnGroupCompleted(notification.StageId, notification.GroupIndex);
                    break;

                case PluginExecutorNotificationType.PluginExecuteStart:
                    if (notification.PluginId is not null)
                        _callback.OnPluginExecuteStart(notification.StageId, notification.PluginId, notification.GroupIndex);
                    break;

                case PluginExecutorNotificationType.PluginExecuteCompleted:
                    if (notification.PluginId is not null)
                        _callback.OnPluginExecuteCompleted(notification.StageId, notification.PluginId, notification.GroupIndex);
                    break;

                case PluginExecutorNotificationType.PluginExecuteFailed:
                    if (notification.PluginId is not null && notification.Exception is not null)
                        _callback.OnPluginExecuteFailed(notification.StageId, notification.PluginId, notification.GroupIndex, notification.Exception);
                    break;

                case PluginExecutorNotificationType.PluginSkipped:
                    if (notification.PluginId is not null && notification.SkipReason is not null)
                        _callback.OnPluginSkipped(notification.StageId, notification.PluginId, notification.GroupIndex, notification.SkipReason);
                    break;
            }
        }
        catch
        {
            // コールバック内の例外は無視（通知処理が本体の処理を妨げないようにする）
        }
    }
}
