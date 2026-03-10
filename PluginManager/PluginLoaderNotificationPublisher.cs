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
        Exception? exception = null)
    {
        // コールバックに通知
        InvokeCallback(notificationType, pluginId, stageId, attempt, configurationFilePath, exception);

        // ロガーに出力
        if (_logger is null)
            return;

        if (exception is not null)
        {
            _logger.LogError(exception,
                "{Message} NotificationType={NotificationType}, PluginId={PluginId}, StageId={StageId}, Attempt={Attempt}, ConfigPath={ConfigPath}",
                message, notificationType, pluginId, stageId, attempt, configurationFilePath);
            return;
        }

        _logger.LogInformation(
            "{Message} NotificationType={NotificationType}, PluginId={PluginId}, StageId={StageId}, Attempt={Attempt}, ConfigPath={ConfigPath}",
            message, notificationType, pluginId, stageId, attempt, configurationFilePath);
    }

    private void InvokeCallback(
        PluginLoaderNotificationType notificationType,
        string? pluginId,
        string? stageId,
        int? attempt,
        string? configurationFilePath,
        Exception? exception)
    {
        if (_callback is null)
            return;

        try
        {
            switch (notificationType)
            {
                case PluginLoaderNotificationType.LoadStart:
                    if (configurationFilePath is not null)
                        _callback.OnLoadStart(configurationFilePath);
                    break;

                case PluginLoaderNotificationType.LoadCompleted:
                    if (configurationFilePath is not null)
                        _callback.OnLoadCompleted(configurationFilePath);
                    break;

                case PluginLoaderNotificationType.PluginLoadStart:
                    if (pluginId is not null && attempt.HasValue)
                        _callback.OnPluginLoadStart(pluginId, attempt.Value);
                    break;

                case PluginLoaderNotificationType.PluginLoadRetry:
                    if (pluginId is not null && attempt.HasValue)
                        _callback.OnPluginLoadRetry(pluginId, attempt.Value, exception);
                    break;

                case PluginLoaderNotificationType.PluginLoadSuccess:
                    if (pluginId is not null && attempt.HasValue)
                        _callback.OnPluginLoadSuccess(pluginId, attempt.Value);
                    break;

                case PluginLoaderNotificationType.PluginLoadFailed:
                    if (pluginId is not null && attempt.HasValue)
                        _callback.OnPluginLoadFailed(pluginId, attempt.Value, exception);
                    break;

                case PluginLoaderNotificationType.ExecuteStart:
                    if (stageId is not null)
                        _callback.OnExecuteStart(stageId);
                    break;

                case PluginLoaderNotificationType.ExecuteCompleted:
                    if (stageId is not null)
                        _callback.OnExecuteCompleted(stageId);
                    break;

                case PluginLoaderNotificationType.ExecuteFailed:
                    if (stageId is not null && exception is not null)
                        _callback.OnExecuteFailed(stageId, exception);
                    break;
            }
        }
        catch
        {
            // コールバック内の例外は無視（通知処理が本体の処理を妨げないようにする）
        }
    }
}
