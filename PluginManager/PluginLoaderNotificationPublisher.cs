using Microsoft.Extensions.Logging;

namespace PluginManager;

/// <summary>
/// PluginLoader のイベント通知とログ出力を担います。
/// </summary>
internal sealed class PluginLoaderNotificationPublisher(ILogger<PluginLoader>? logger)
{
    private readonly ILogger<PluginLoader>? _logger = logger;

    public void Publish(
        object sender,
        EventHandler<PluginLoaderEventArgs>? pluginEvent,
        PluginLoaderEventType eventType,
        string message,
        string? pluginId = null,
        string? stageId = null,
        int? attempt = null,
        string? configurationFilePath = null,
        Exception? exception = null)
    {
        var args = new PluginLoaderEventArgs(eventType, message, pluginId, stageId, attempt, configurationFilePath, exception);
        pluginEvent?.Invoke(sender, args);

        if (_logger is null)
            return;

        if (exception is not null)
        {
            _logger.LogError(exception,
                "{Message} EventType={EventType}, PluginId={PluginId}, StageId={StageId}, Attempt={Attempt}, ConfigPath={ConfigPath}",
                message, eventType, pluginId, stageId, attempt, configurationFilePath);
            return;
        }

        _logger.LogInformation(
            "{Message} EventType={EventType}, PluginId={PluginId}, StageId={StageId}, Attempt={Attempt}, ConfigPath={ConfigPath}",
            message, eventType, pluginId, stageId, attempt, configurationFilePath);
    }
}
