using PluginHost;
using PluginManager.Ipc;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="PluginHostNotifier"/> の通知ペイロード検証テストです。
/// </summary>
public sealed class PluginHostNotifierTests
{
    [Fact]
    public void Notify_WithAllFields_EnqueuesNotificationWithExpectedPayload()
    {
        var mapName = $"PluginHostNotifierTests_{Guid.NewGuid():N}";
        using var writer = new MemoryMappedNotificationQueue(mapName);
        using var reader = new MemoryMappedNotificationQueue(mapName);
        var notifier = new PluginHostNotifier(writer);

        notifier.Notify(
            PluginProcessNotificationType.ExecuteFailed,
            "実行失敗",
            requestId: "req-1",
            pluginId: "plugin-a",
            stageId: "Processing",
            errorType: nameof(InvalidOperationException),
            errorMessage: "失敗しました");

        var notifications = reader.Drain();

        var notification = Assert.Single(notifications);
        Assert.Equal(PluginProcessNotificationType.ExecuteFailed, notification.NotificationType);
        Assert.Equal("実行失敗", notification.Message);
        Assert.Equal("req-1", notification.RequestId);
        Assert.Equal("plugin-a", notification.PluginId);
        Assert.Equal("Processing", notification.StageId);
        Assert.Equal(nameof(InvalidOperationException), notification.ErrorType);
        Assert.Equal("失敗しました", notification.ErrorMessage);
        Assert.True(notification.ProcessId > 0);
    }

    [Fact]
    public void Notify_WithoutQueue_DoesNotThrow()
    {
        var notifier = new PluginHostNotifier(notificationQueue: null);

        var exception = Record.Exception(() =>
            notifier.Notify(PluginProcessNotificationType.HostStarted, "開始"));

        Assert.Null(exception);
    }
}
