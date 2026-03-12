using PluginManager.Ipc;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="MemoryMappedNotificationQueue"/> のテストです。
/// </summary>
public sealed class MemoryMappedNotificationQueueTests
{
    [Fact]
    public void Enqueue_Drain_ReturnsNotificationsInOrder()
    {
        var mapName = $"PluginManagerTest_{Guid.NewGuid():N}";
        using var writer = new MemoryMappedNotificationQueue(mapName);
        using var reader = new MemoryMappedNotificationQueue(mapName);

        writer.Enqueue(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.ExecuteStarted,
            Message = "開始",
            PluginId = "plugin-a",
            StageId = "Processing",
            ProcessId = 1,
        });
        writer.Enqueue(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.ExecuteCompleted,
            Message = "完了",
            PluginId = "plugin-a",
            StageId = "Processing",
            ProcessId = 1,
        });

        var notifications = reader.Drain();

        Assert.Collection(
            notifications,
            first =>
            {
                Assert.Equal(PluginProcessNotificationType.ExecuteStarted, first.NotificationType);
                Assert.Equal("開始", first.Message);
            },
            second =>
            {
                Assert.Equal(PluginProcessNotificationType.ExecuteCompleted, second.NotificationType);
                Assert.Equal("完了", second.Message);
            });
    }

    [Fact]
    public void Drain_AfterRead_ReturnsEmpty()
    {
        var mapName = $"PluginManagerTest_{Guid.NewGuid():N}";
        using var queue = new MemoryMappedNotificationQueue(mapName);

        queue.Enqueue(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.LoadCompleted,
            Message = "ロード完了",
            PluginId = "plugin-a",
            ProcessId = 1,
        });

        _ = queue.Drain();
        var notifications = queue.Drain();

        Assert.Empty(notifications);
    }
}
