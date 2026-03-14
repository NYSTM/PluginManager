using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="PluginLoaderNotificationPublisher"/> のテストです。
/// </summary>
public sealed class PluginLoaderNotificationPublisherTests
{
    [Fact]
    public void Publish_TypedNotifications_InvokesExpectedCallbacks()
    {
        var callback = new TestCallback();
        var publisher = new PluginLoaderNotificationPublisher(logger: null);
        var retryException = new InvalidOperationException("リトライ");
        var failedException = new InvalidOperationException("失敗");
        var executeException = new InvalidOperationException("実行失敗");

        publisher.SetCallback(callback);
        publisher.Publish(PluginLoaderNotificationType.LoadStart, "開始", configurationFilePath: "config.json", executionId: "exec-1");
        publisher.Publish(PluginLoaderNotificationType.LoadCompleted, "完了", configurationFilePath: "config.json", executionId: "exec-1");
        publisher.Publish(PluginLoaderNotificationType.PluginLoadStart, "読込開始", pluginId: "plugin-a", attempt: 1);
        publisher.Publish(PluginLoaderNotificationType.PluginLoadRetry, "再試行", pluginId: "plugin-a", attempt: 2, exception: retryException);
        publisher.Publish(PluginLoaderNotificationType.PluginLoadSuccess, "成功", pluginId: "plugin-a", attempt: 3);
        publisher.Publish(PluginLoaderNotificationType.PluginLoadFailed, "失敗", pluginId: "plugin-a", attempt: 4, exception: failedException);
        publisher.Publish(PluginLoaderNotificationType.ExecuteStart, "実行開始", stageId: "Processing");
        publisher.Publish(PluginLoaderNotificationType.ExecuteCompleted, "実行完了", stageId: "Processing");
        publisher.Publish(PluginLoaderNotificationType.ExecuteFailed, "実行失敗", stageId: "Processing", exception: executeException);

        Assert.Equal(9, callback.Notifications.Count);
        Assert.Equal("config.json", callback.LoadStartPath);
        Assert.Equal("config.json", callback.LoadCompletedPath);
        Assert.Equal(("plugin-a", 1), callback.PluginLoadStartArgs);
        Assert.Equal(("plugin-a", 2, retryException), callback.PluginLoadRetryArgs);
        Assert.Equal(("plugin-a", 3), callback.PluginLoadSuccessArgs);
        Assert.Equal(("plugin-a", 4, failedException), callback.PluginLoadFailedArgs);
        Assert.Equal("Processing", callback.ExecuteStartStageId);
        Assert.Equal("Processing", callback.ExecuteCompletedStageId);
        Assert.Equal(("Processing", executeException), callback.ExecuteFailedArgs);
        Assert.All(callback.Notifications, notification => Assert.NotNull(notification.Message));
    }

    [Fact]
    public void Publish_MissingRequiredValues_SkipsTypedCallbacks()
    {
        var callback = new TestCallback();
        var publisher = new PluginLoaderNotificationPublisher(logger: null);

        publisher.SetCallback(callback);
        publisher.Publish(PluginLoaderNotificationType.LoadStart, "開始");
        publisher.Publish(PluginLoaderNotificationType.LoadCompleted, "完了");
        publisher.Publish(PluginLoaderNotificationType.PluginLoadStart, "読込開始", pluginId: "plugin-a");
        publisher.Publish(PluginLoaderNotificationType.PluginLoadRetry, "再試行", pluginId: "plugin-a");
        publisher.Publish(PluginLoaderNotificationType.PluginLoadSuccess, "成功", attempt: 2);
        publisher.Publish(PluginLoaderNotificationType.PluginLoadFailed, "失敗", pluginId: "plugin-a");
        publisher.Publish(PluginLoaderNotificationType.ExecuteStart, "実行開始");
        publisher.Publish(PluginLoaderNotificationType.ExecuteCompleted, "実行完了");
        publisher.Publish(PluginLoaderNotificationType.ExecuteFailed, "実行失敗", stageId: "Processing");

        Assert.Equal(9, callback.Notifications.Count);
        Assert.Null(callback.LoadStartPath);
        Assert.Null(callback.LoadCompletedPath);
        Assert.Null(callback.PluginLoadStartArgs);
        Assert.Null(callback.PluginLoadRetryArgs);
        Assert.Null(callback.PluginLoadSuccessArgs);
        Assert.Null(callback.PluginLoadFailedArgs);
        Assert.Null(callback.ExecuteStartStageId);
        Assert.Null(callback.ExecuteCompletedStageId);
        Assert.Null(callback.ExecuteFailedArgs);
    }

    [Fact]
    public void Publish_WhenOnlyAttemptIsProvided_SkipsPluginCallbacks()
    {
        var callback = new TestCallback();
        var publisher = new PluginLoaderNotificationPublisher(logger: null);

        publisher.SetCallback(callback);
        publisher.Publish(PluginLoaderNotificationType.PluginLoadStart, "読込開始", attempt: 1);
        publisher.Publish(PluginLoaderNotificationType.PluginLoadRetry, "再試行", attempt: 2, exception: new InvalidOperationException("再試行"));
        publisher.Publish(PluginLoaderNotificationType.PluginLoadFailed, "失敗", attempt: 3, exception: new InvalidOperationException("失敗"));
        publisher.Publish(PluginLoaderNotificationType.ExecuteFailed, "実行失敗", exception: new InvalidOperationException("実行失敗"));

        Assert.Null(callback.PluginLoadStartArgs);
        Assert.Null(callback.PluginLoadRetryArgs);
        Assert.Null(callback.PluginLoadFailedArgs);
        Assert.Null(callback.ExecuteFailedArgs);
    }

    [Fact]
    public void Publish_WhenCallbackThrows_SwallowsException()
    {
        var publisher = new PluginLoaderNotificationPublisher(logger: null);
        publisher.SetCallback(new ThrowingCallback());

        var ex = Record.Exception(() =>
            publisher.Publish(PluginLoaderNotificationType.LoadStart, "開始", configurationFilePath: "config.json"));

        Assert.Null(ex);
    }

    private sealed class TestCallback : IPluginLoaderCallback
    {
        public List<PluginLoaderNotification> Notifications { get; } = [];
        public string? LoadStartPath { get; private set; }
        public string? LoadCompletedPath { get; private set; }
        public (string PluginId, int Attempt)? PluginLoadStartArgs { get; private set; }
        public (string PluginId, int Attempt, Exception? Error)? PluginLoadRetryArgs { get; private set; }
        public (string PluginId, int Attempt)? PluginLoadSuccessArgs { get; private set; }
        public (string PluginId, int Attempt, Exception? Error)? PluginLoadFailedArgs { get; private set; }
        public string? ExecuteStartStageId { get; private set; }
        public string? ExecuteCompletedStageId { get; private set; }
        public (string StageId, Exception Error)? ExecuteFailedArgs { get; private set; }

        public void OnNotification(PluginLoaderNotification notification)
            => Notifications.Add(notification);

        public void OnLoadStart(string configurationFilePath)
            => LoadStartPath = configurationFilePath;

        public void OnLoadCompleted(string configurationFilePath)
            => LoadCompletedPath = configurationFilePath;

        public void OnPluginLoadStart(string pluginId, int attempt)
            => PluginLoadStartArgs = (pluginId, attempt);

        public void OnPluginLoadRetry(string pluginId, int attempt, Exception? error)
            => PluginLoadRetryArgs = (pluginId, attempt, error);

        public void OnPluginLoadSuccess(string pluginId, int attempt)
            => PluginLoadSuccessArgs = (pluginId, attempt);

        public void OnPluginLoadFailed(string pluginId, int attempt, Exception? error)
            => PluginLoadFailedArgs = (pluginId, attempt, error);

        public void OnExecuteStart(string stageId)
            => ExecuteStartStageId = stageId;

        public void OnExecuteCompleted(string stageId)
            => ExecuteCompletedStageId = stageId;

        public void OnExecuteFailed(string stageId, Exception error)
            => ExecuteFailedArgs = (stageId, error);
    }

    private sealed class ThrowingCallback : IPluginLoaderCallback
    {
        public void OnNotification(PluginLoaderNotification notification)
            => throw new InvalidOperationException("コールバック失敗");
    }
}
