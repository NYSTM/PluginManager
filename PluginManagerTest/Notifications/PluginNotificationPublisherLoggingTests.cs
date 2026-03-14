using Microsoft.Extensions.Logging;
using PluginManager;
using PluginManager.Ipc;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// 通知 Publisher のログ出力テストです。
/// </summary>
public sealed class PluginNotificationPublisherLoggingTests
{
    [Fact]
    public void PluginProcessNotificationPublisher_Publish_WritesInformationAndErrorLogs()
    {
        var logger = new TestLogger();
        var publisher = new PluginProcessNotificationPublisher(logger);

        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.LoadCompleted,
            Message = "ロード完了",
            PluginId = "plugin-a",
            ProcessId = 1,
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.LoadFailed,
            Message = "ロード失敗",
            PluginId = "plugin-a",
            ProcessId = 1,
            ErrorMessage = "error",
            ErrorType = nameof(InvalidOperationException),
        });

        Assert.Contains(logger.Entries, x => x.Level == LogLevel.Information && x.Message.Contains("ロード完了"));
        Assert.Contains(logger.Entries, x => x.Level == LogLevel.Error && x.Message.Contains("ロード失敗"));
    }

    [Fact]
    public void PluginExecutorNotificationPublisher_Publish_WritesInformationAndErrorLogs()
    {
        var logger = new TestLogger();
        var publisher = new PluginExecutorNotificationPublisher(logger);

        publisher.Publish(PluginExecutorNotificationType.GroupCompleted, "完了", "Processing", 1, executionId: "exec-1");
        publisher.Publish(PluginExecutorNotificationType.PluginExecuteFailed, "失敗", "Processing", 1, pluginId: "plugin-a", exception: new InvalidOperationException("ng"));

        Assert.Contains(logger.Entries, x => x.Level == LogLevel.Information && x.Message.Contains("完了"));
        Assert.Contains(logger.Entries, x => x.Level == LogLevel.Error && x.Message.Contains("失敗"));
    }

    [Fact]
    public void PluginLoaderNotificationPublisher_Publish_WritesInformationAndErrorLogs()
    {
        var logger = new TestLogger();
        var publisher = new PluginLoaderNotificationPublisher(logger);

        publisher.Publish(PluginLoaderNotificationType.LoadCompleted, "ロード完了", configurationFilePath: "config.json", executionId: "exec-1");
        publisher.Publish(PluginLoaderNotificationType.PluginLoadFailed, "ロード失敗", pluginId: "plugin-a", attempt: 2, exception: new InvalidOperationException("ng"), executionId: "exec-2");

        Assert.Contains(logger.Entries, x => x.Level == LogLevel.Information && x.Message.Contains("ロード完了"));
        Assert.Contains(logger.Entries, x => x.Level == LogLevel.Error && x.Message.Contains("ロード失敗"));
    }

    private sealed class TestLogger : ILogger<PluginLoader>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}
