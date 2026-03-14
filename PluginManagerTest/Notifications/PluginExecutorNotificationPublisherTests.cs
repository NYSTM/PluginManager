using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="PluginExecutorNotificationPublisher"/> のテストです。
/// </summary>
public sealed class PluginExecutorNotificationPublisherTests
{
    [Fact]
    public void Publish_TypedNotifications_InvokesExpectedCallbacks()
    {
        var callback = new TestCallback();
        var publisher = new PluginExecutorNotificationPublisher(logger: null);
        var executeException = new InvalidOperationException("実行失敗");

        publisher.SetCallback(callback);
        publisher.Publish(PluginExecutorNotificationType.GroupStart, "開始", "Processing", 1, executionId: "exec-1");
        publisher.Publish(PluginExecutorNotificationType.GroupCompleted, "完了", "Processing", 1, executionId: "exec-1");
        publisher.Publish(PluginExecutorNotificationType.PluginExecuteStart, "実行開始", "Processing", 1, pluginId: "plugin-a", executionId: "exec-1");
        publisher.Publish(PluginExecutorNotificationType.PluginExecuteCompleted, "実行完了", "Processing", 1, pluginId: "plugin-a", executionId: "exec-1");
        publisher.Publish(PluginExecutorNotificationType.PluginExecuteFailed, "実行失敗", "Processing", 1, pluginId: "plugin-a", executionId: "exec-1", exception: executeException);
        publisher.Publish(PluginExecutorNotificationType.PluginSkipped, "スキップ", "Processing", 1, pluginId: "plugin-a", executionId: "exec-1", skipReason: "対象外");

        Assert.Equal(6, callback.Notifications.Count);
        Assert.Equal(("Processing", 1), callback.LastGroupStart);
        Assert.Equal(("Processing", 1), callback.LastGroupCompleted);
        Assert.Equal(("Processing", "plugin-a", 1), callback.LastPluginExecuteStart);
        Assert.Equal(("Processing", "plugin-a", 1), callback.LastPluginExecuteCompleted);
        Assert.Equal(("Processing", "plugin-a", 1, executeException), callback.LastPluginExecuteFailed);
        Assert.Equal(("Processing", "plugin-a", 1, "対象外"), callback.LastPluginSkipped);
    }

    [Fact]
    public void Publish_MissingRequiredValues_SkipsTypedCallbacks()
    {
        var callback = new TestCallback();
        var publisher = new PluginExecutorNotificationPublisher(logger: null);

        publisher.SetCallback(callback);
        publisher.Publish(PluginExecutorNotificationType.PluginExecuteStart, "実行開始", "Processing", 1);
        publisher.Publish(PluginExecutorNotificationType.PluginExecuteCompleted, "実行完了", "Processing", 1);
        publisher.Publish(PluginExecutorNotificationType.PluginExecuteFailed, "実行失敗", "Processing", 1, pluginId: "plugin-a");
        publisher.Publish(PluginExecutorNotificationType.PluginExecuteFailed, "実行失敗", "Processing", 1, exception: new InvalidOperationException("失敗"));
        publisher.Publish(PluginExecutorNotificationType.PluginSkipped, "スキップ", "Processing", 1, pluginId: "plugin-a");
        publisher.Publish(PluginExecutorNotificationType.PluginSkipped, "スキップ", "Processing", 1, skipReason: "対象外");

        Assert.Equal(6, callback.Notifications.Count);
        Assert.Null(callback.LastPluginExecuteStart);
        Assert.Null(callback.LastPluginExecuteCompleted);
        Assert.Null(callback.LastPluginExecuteFailed);
        Assert.Null(callback.LastPluginSkipped);
    }

    [Fact]
    public void Publish_WhenCallbackThrows_SwallowsException()
    {
        var publisher = new PluginExecutorNotificationPublisher(logger: null);
        publisher.SetCallback(new ThrowingCallback());

        var ex = Record.Exception(() =>
            publisher.Publish(PluginExecutorNotificationType.GroupStart, "開始", "Processing", 1));

        Assert.Null(ex);
    }

    private sealed class TestCallback : IPluginExecutorCallback
    {
        public List<PluginExecutorNotification> Notifications { get; } = [];
        public (string StageId, int GroupIndex)? LastGroupStart { get; private set; }
        public (string StageId, int GroupIndex)? LastGroupCompleted { get; private set; }
        public (string StageId, string PluginId, int GroupIndex)? LastPluginExecuteStart { get; private set; }
        public (string StageId, string PluginId, int GroupIndex)? LastPluginExecuteCompleted { get; private set; }
        public (string StageId, string PluginId, int GroupIndex, Exception Error)? LastPluginExecuteFailed { get; private set; }
        public (string StageId, string PluginId, int GroupIndex, string SkipReason)? LastPluginSkipped { get; private set; }

        public void OnNotification(PluginExecutorNotification notification)
            => Notifications.Add(notification);

        public void OnGroupStart(string stageId, int groupIndex)
            => LastGroupStart = (stageId, groupIndex);

        public void OnGroupCompleted(string stageId, int groupIndex)
            => LastGroupCompleted = (stageId, groupIndex);

        public void OnPluginExecuteStart(string stageId, string pluginId, int groupIndex)
            => LastPluginExecuteStart = (stageId, pluginId, groupIndex);

        public void OnPluginExecuteCompleted(string stageId, string pluginId, int groupIndex)
            => LastPluginExecuteCompleted = (stageId, pluginId, groupIndex);

        public void OnPluginExecuteFailed(string stageId, string pluginId, int groupIndex, Exception error)
            => LastPluginExecuteFailed = (stageId, pluginId, groupIndex, error);

        public void OnPluginSkipped(string stageId, string pluginId, int groupIndex, string skipReason)
            => LastPluginSkipped = (stageId, pluginId, groupIndex, skipReason);
    }

    private sealed class ThrowingCallback : IPluginExecutorCallback
    {
        public void OnNotification(PluginExecutorNotification notification)
            => throw new InvalidOperationException("コールバック失敗");
    }
}
