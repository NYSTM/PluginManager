using Microsoft.Extensions.Logging;

namespace PluginManager;

/// <summary>
/// PluginLoader のコールバック通知とログ出力を担います。
/// </summary>
internal sealed class PluginLoaderNotificationPublisher(ILogger<PluginLoader>? logger)
{
    private readonly ILogger<PluginLoader>? _logger = logger;
    private IPluginLoaderCallback? _callback;

    /// <summary>
    /// コールバックを設定します。
    /// </summary>
    public void SetCallback(IPluginLoaderCallback? callback)
        => _callback = callback;

    public void Publish(
        PluginLoaderNotificationType notificationType,
        string message,
        string? pluginId = null,
        string? stageId = null,
        int? attempt = null,
        string? configurationFilePath = null,
        Exception? exception = null,
        string? executionId = null)
    {
        var notification = new PluginLoaderNotification(
            notificationType,
            message,
            pluginId,
            stageId,
            attempt,
            configurationFilePath,
            exception,
            executionId);

        // コールバックに通知
        InvokeCallback(notification);

        // ロガーに出力
        if (_logger is null)
            return;

        if (exception is not null)
        {
            _logger.LogError(exception,
                "{Message} NotificationType={NotificationType}, PluginId={PluginId}, StageId={StageId}, Attempt={Attempt}, ConfigPath={ConfigPath}, ExecutionId={ExecutionId}",
                message, notificationType, pluginId, stageId, attempt, configurationFilePath, executionId);
            return;
        }

        _logger.LogInformation(
            "{Message} NotificationType={NotificationType}, PluginId={PluginId}, StageId={StageId}, Attempt={Attempt}, ConfigPath={ConfigPath}, ExecutionId={ExecutionId}",
            message, notificationType, pluginId, stageId, attempt, configurationFilePath, executionId);
    }

    private void InvokeCallback(PluginLoaderNotification notification)
    {
        if (_callback is null)
            return;

        try
        {
            _callback.OnNotification(notification);

            switch (notification.NotificationType)
            {
                case PluginLoaderNotificationType.LoadStart:
                    if (notification.ConfigurationFilePath is not null)
                        _callback.OnLoadStart(notification.ConfigurationFilePath);
                    break;

                case PluginLoaderNotificationType.LoadCompleted:
                    if (notification.ConfigurationFilePath is not null)
                        _callback.OnLoadCompleted(notification.ConfigurationFilePath);
                    break;

                case PluginLoaderNotificationType.PluginLoadStart:
                    if (notification.PluginId is not null && notification.Attempt.HasValue)
                        _callback.OnPluginLoadStart(notification.PluginId, notification.Attempt.Value);
                    break;

                case PluginLoaderNotificationType.PluginLoadRetry:
                    if (notification.PluginId is not null && notification.Attempt.HasValue)
                        _callback.OnPluginLoadRetry(notification.PluginId, notification.Attempt.Value, notification.Exception);
                    break;

                case PluginLoaderNotificationType.PluginLoadSuccess:
                    if (notification.PluginId is not null && notification.Attempt.HasValue)
                        _callback.OnPluginLoadSuccess(notification.PluginId, notification.Attempt.Value);
                    break;

                case PluginLoaderNotificationType.PluginLoadFailed:
                    if (notification.PluginId is not null && notification.Attempt.HasValue)
                        _callback.OnPluginLoadFailed(notification.PluginId, notification.Attempt.Value, notification.Exception);
                    break;

                case PluginLoaderNotificationType.ExecuteStart:
                    if (notification.StageId is not null)
                        _callback.OnExecuteStart(notification.StageId);
                    break;

                case PluginLoaderNotificationType.ExecuteCompleted:
                    if (notification.StageId is not null)
                        _callback.OnExecuteCompleted(notification.StageId);
                    break;

                case PluginLoaderNotificationType.ExecuteFailed:
                    if (notification.StageId is not null && notification.Exception is not null)
                        _callback.OnExecuteFailed(notification.StageId, notification.Exception);
                    break;
            }
        }
        catch
        {
            // コールバック内の例外は無視（通知処理が本体の処理を妨げないようにする）
        }
    }
}
