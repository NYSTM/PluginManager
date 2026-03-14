using PluginManager;
using PluginManager.Ipc;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="PluginProcessNotificationPublisher"/> のコールバック通知テストです。
/// </summary>
public sealed class PluginProcessCallbackTests
{
    [Fact]
    public void Publish_TypedNotifications_InvokesExpectedCallbacks()
    {
        var callback = new TestCallback();
        var publisher = new PluginProcessNotificationPublisher(logger: null);

        publisher.SetCallback(callback);

        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.HostStarted,
            Message = "ホスト起動",
            ProcessId = 1234,
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.LoadCompleted,
            Message = "ロード完了",
            PluginId = "plugin-a",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.LoadFailed,
            Message = "ロード失敗",
            PluginId = "plugin-b",
            ErrorMessage = "load error",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.InitializeStarted,
            Message = "初期化開始",
            PluginId = "plugin-c",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.InitializeCompleted,
            Message = "初期化完了",
            PluginId = "plugin-c",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.InitializeFailed,
            Message = "初期化失敗",
            PluginId = "plugin-c",
            ErrorMessage = "init error",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.ExecuteStarted,
            Message = "実行開始",
            PluginId = "plugin-d",
            StageId = "Processing",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.ExecuteCompleted,
            Message = "実行完了",
            PluginId = "plugin-d",
            StageId = "Processing",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.ExecuteFailed,
            Message = "実行失敗",
            PluginId = "plugin-d",
            StageId = "Processing",
            ErrorMessage = "execute error",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.UnloadCompleted,
            Message = "アンロード完了",
            PluginId = "plugin-e",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.UnloadFailed,
            Message = "アンロード失敗",
            PluginId = "plugin-e",
            ErrorMessage = "unload error",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.ShutdownReceived,
            Message = "シャットダウン受信",
        });

        Assert.Equal(12, callback.Notifications.Count);
        Assert.Equal(1234, callback.HostStartedProcessId);
        Assert.Equal("plugin-a", callback.LoadCompletedPluginId);
        Assert.Equal(("plugin-b", "load error"), callback.LoadFailedArgs);
        Assert.Equal("plugin-c", callback.InitializeStartedPluginId);
        Assert.Equal("plugin-c", callback.InitializeCompletedPluginId);
        Assert.Equal(("plugin-c", "init error"), callback.InitializeFailedArgs);
        Assert.Equal(("plugin-d", "Processing"), callback.ExecuteStartedArgs);
        Assert.Equal(("plugin-d", "Processing"), callback.ExecuteCompletedArgs);
        Assert.Equal(("plugin-d", "Processing", "execute error"), callback.ExecuteFailedArgs);
        Assert.Equal("plugin-e", callback.UnloadCompletedPluginId);
        Assert.Equal(("plugin-e", "unload error"), callback.UnloadFailedArgs);
        Assert.True(callback.ShutdownReceivedCalled);
    }

    [Fact]
    public void Publish_MissingPluginId_SkipsPluginSpecificCallbacks()
    {
        var callback = new TestCallback();
        var publisher = new PluginProcessNotificationPublisher(logger: null);

        publisher.SetCallback(callback);

        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.LoadCompleted,
            Message = "ロード完了",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.LoadFailed,
            Message = "ロード失敗",
            ErrorMessage = "error",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.InitializeStarted,
            Message = "初期化開始",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.InitializeCompleted,
            Message = "初期化完了",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.InitializeFailed,
            Message = "初期化失敗",
            ErrorMessage = "error",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.ExecuteStarted,
            Message = "実行開始",
            StageId = "Processing",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.ExecuteCompleted,
            Message = "実行完了",
            StageId = "Processing",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.ExecuteFailed,
            Message = "実行失敗",
            StageId = "Processing",
            ErrorMessage = "error",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.UnloadCompleted,
            Message = "アンロード完了",
        });
        publisher.Publish(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.UnloadFailed,
            Message = "アンロード失敗",
            ErrorMessage = "error",
        });

        Assert.Equal(10, callback.Notifications.Count);
        Assert.Null(callback.LoadCompletedPluginId);
        Assert.Null(callback.LoadFailedArgs);
        Assert.Null(callback.InitializeStartedPluginId);
        Assert.Null(callback.InitializeCompletedPluginId);
        Assert.Null(callback.InitializeFailedArgs);
        Assert.Null(callback.ExecuteStartedArgs);
        Assert.Null(callback.ExecuteCompletedArgs);
        Assert.Null(callback.ExecuteFailedArgs);
        Assert.Null(callback.UnloadCompletedPluginId);
        Assert.Null(callback.UnloadFailedArgs);
    }

    [Fact]
    public void Publish_WhenCallbackThrows_SwallowsException()
    {
        var publisher = new PluginProcessNotificationPublisher(logger: null);
        publisher.SetCallback(new ThrowingCallback());

        var ex = Record.Exception(() =>
            publisher.Publish(new PluginProcessNotification
            {
                NotificationType = PluginProcessNotificationType.HostStarted,
                Message = "開始",
                ProcessId = 1,
            }));

        Assert.Null(ex);
    }

    [Fact]
    public void Publish_WhenNotificationIsNull_ThrowsArgumentNullException()
    {
        var publisher = new PluginProcessNotificationPublisher(logger: null);

        Assert.Throws<ArgumentNullException>(() => publisher.Publish(null!));
    }

    private sealed class TestCallback : IPluginProcessCallback
    {
        public List<PluginProcessNotification> Notifications { get; } = [];

        public int? HostStartedProcessId { get; private set; }
        public string? LoadCompletedPluginId { get; private set; }
        public (string PluginId, string? ErrorMessage)? LoadFailedArgs { get; private set; }
        public string? InitializeStartedPluginId { get; private set; }
        public string? InitializeCompletedPluginId { get; private set; }
        public (string PluginId, string? ErrorMessage)? InitializeFailedArgs { get; private set; }
        public (string PluginId, string? StageId)? ExecuteStartedArgs { get; private set; }
        public (string PluginId, string? StageId)? ExecuteCompletedArgs { get; private set; }
        public (string PluginId, string? StageId, string? ErrorMessage)? ExecuteFailedArgs { get; private set; }
        public string? UnloadCompletedPluginId { get; private set; }
        public (string PluginId, string? ErrorMessage)? UnloadFailedArgs { get; private set; }
        public bool ShutdownReceivedCalled { get; private set; }

        public void OnNotification(PluginProcessNotification notification)
            => Notifications.Add(notification);

        public void OnHostStarted(int processId)
            => HostStartedProcessId = processId;

        public void OnLoadCompleted(string pluginId)
            => LoadCompletedPluginId = pluginId;

        public void OnLoadFailed(string pluginId, string? errorMessage)
            => LoadFailedArgs = (pluginId, errorMessage);

        public void OnInitializeStarted(string pluginId)
            => InitializeStartedPluginId = pluginId;

        public void OnInitializeCompleted(string pluginId)
            => InitializeCompletedPluginId = pluginId;

        public void OnInitializeFailed(string pluginId, string? errorMessage)
            => InitializeFailedArgs = (pluginId, errorMessage);

        public void OnExecuteStarted(string pluginId, string? stageId)
            => ExecuteStartedArgs = (pluginId, stageId);

        public void OnExecuteCompleted(string pluginId, string? stageId)
            => ExecuteCompletedArgs = (pluginId, stageId);

        public void OnExecuteFailed(string pluginId, string? stageId, string? errorMessage)
            => ExecuteFailedArgs = (pluginId, stageId, errorMessage);

        public void OnUnloadCompleted(string pluginId)
            => UnloadCompletedPluginId = pluginId;

        public void OnUnloadFailed(string pluginId, string? errorMessage)
            => UnloadFailedArgs = (pluginId, errorMessage);

        public void OnShutdownReceived()
            => ShutdownReceivedCalled = true;
    }

    private sealed class ThrowingCallback : IPluginProcessCallback
    {
        public void OnNotification(PluginProcessNotification notification)
            => throw new InvalidOperationException("コールバック失敗");
    }
}
