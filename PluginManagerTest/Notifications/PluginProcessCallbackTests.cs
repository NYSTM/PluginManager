using PluginManager;
using PluginManager.Ipc;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="IPluginProcessCallback"/> のテストです。
/// </summary>
public sealed class PluginProcessCallbackTests
{
    [Fact]
    public void Publish_HostStarted_InvokesTypedCallback()
    {
        var callback = new TestCallback();
        var publisher = new PluginProcessNotificationPublisher(logger: null);
        publisher.SetCallback(callback);

        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.HostStarted,
            Message = "起動",
            ProcessId = 1234,
        });

        Assert.Equal(1234, callback.HostStartedProcessId);
        Assert.Single(callback.Notifications);
    }

    [Fact]
    public void Publish_ExecuteFailed_InvokesTypedCallback()
    {
        var callback = new TestCallback();
        var publisher = new PluginProcessNotificationPublisher(logger: null);
        publisher.SetCallback(callback);

        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.ExecuteFailed,
            Message = "失敗",
            PluginId = "plugin-a",
            StageId = "Processing",
            ProcessId = 1,
            ErrorMessage = "実行失敗",
        });

        Assert.Equal(("plugin-a", "Processing", "実行失敗"), callback.LastExecuteFailed);
    }

    [Fact]
    public void Publish_CallbackThrows_DoesNotThrow()
    {
        var publisher = new PluginProcessNotificationPublisher(logger: null);
        publisher.SetCallback(new ThrowingCallback());

        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.LoadCompleted,
            Message = "完了",
            PluginId = "plugin-a",
            ProcessId = 1,
        });
    }

    private sealed class TestCallback : IPluginProcessCallback
    {
        public List<PluginProcessNotification> Notifications { get; } = [];
        public int? HostStartedProcessId { get; private set; }
        public (string PluginId, string? StageId, string? ErrorMessage)? LastExecuteFailed { get; private set; }

        public void OnNotification(PluginProcessNotification notification)
            => Notifications.Add(notification);

        public void OnHostStarted(int processId)
            => HostStartedProcessId = processId;

        public void OnExecuteFailed(string pluginId, string? stageId, string? errorMessage)
            => LastExecuteFailed = (pluginId, stageId, errorMessage);
    }

    private sealed class ThrowingCallback : IPluginProcessCallback
    {
        public void OnNotification(PluginProcessNotification notification)
            => throw new InvalidOperationException("コールバック例外");
    }
}
