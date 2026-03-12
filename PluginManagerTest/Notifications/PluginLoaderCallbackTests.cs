using Xunit;
using PluginManager;

namespace PluginManagerTest;

/// <summary>
/// <see cref="IPluginLoaderCallback"/> のテストクラスです。
/// </summary>
public class PluginLoaderCallbackTests
{
    [Fact]
    public async Task SetCallback_CallbackReceivesNotifications()
    {
        // Arrange
        using var loader = new PluginLoader();
        var context = new PluginContext();
        var callback = new TestCallback();
        loader.SetCallback(callback);

        var tempFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(tempFile, """
        {
          "PluginsPath": "nonexistent",
          "IntervalMilliseconds": 0,
          "TimeoutMilliseconds": 0
        }
        """);

        try
        {
            // Act
            await loader.LoadFromConfigurationAsync(tempFile, context);

            // Assert
            Assert.True(callback.OnLoadStartCalled);
            Assert.True(callback.OnLoadCompletedCalled);
            Assert.Equal(tempFile, callback.LastConfigPath);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SetCallback_Null_RemovesCallback()
    {
        // Arrange
        using var loader = new PluginLoader();
        var context = new PluginContext();
        var callback = new TestCallback();
        loader.SetCallback(callback);
        loader.SetCallback(null); // コールバック解除

        var tempFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(tempFile, """
        {
          "PluginsPath": "nonexistent",
          "IntervalMilliseconds": 0,
          "TimeoutMilliseconds": 0
        }
        """);

        try
        {
            // Act
            await loader.LoadFromConfigurationAsync(tempFile, context);

            // Assert: コールバックは呼ばれない
            Assert.False(callback.OnLoadStartCalled);
            Assert.False(callback.OnLoadCompletedCalled);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Callback_ExceptionInCallback_DoesNotThrow()
    {
        // Arrange
        using var loader = new PluginLoader();
        var context = new PluginContext();
        var callback = new ThrowingCallback();
        loader.SetCallback(callback);

        var tempFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(tempFile, """
        {
          "PluginsPath": "nonexistent",
          "IntervalMilliseconds": 0,
          "TimeoutMilliseconds": 0
        }
        """);

        try
        {
            // Act: コールバック内で例外がスローされても処理は継続する
            await loader.LoadFromConfigurationAsync(tempFile, context);

            // Assert: 例外が外に伝播しないことを確認
            Assert.True(true); // 到達できることを確認
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SetCallback_NotificationContainsExecutionId()
    {
        // Arrange
        using var loader = new PluginLoader();
        var context = new PluginContext();
        var callback = new TestCallback();
        loader.SetCallback(callback);

        var tempFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(tempFile, """
        {
          "PluginsPath": "nonexistent",
          "IntervalMilliseconds": 0,
          "TimeoutMilliseconds": 0
        }
        """);

        try
        {
            // Act
            await loader.LoadFromConfigurationAsync(tempFile, context);

            // Assert
            Assert.NotEmpty(callback.Notifications);
            Assert.All(callback.Notifications, n => Assert.False(string.IsNullOrWhiteSpace(n.ExecutionId)));

            var loadStart = callback.Notifications.First(n => n.NotificationType == PluginLoaderNotificationType.LoadStart);
            var loadCompleted = callback.Notifications.First(n => n.NotificationType == PluginLoaderNotificationType.LoadCompleted);
            Assert.Equal(loadStart.ExecutionId, loadCompleted.ExecutionId);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private class TestCallback : IPluginLoaderCallback
    {
        public bool OnLoadStartCalled { get; private set; }
        public bool OnLoadCompletedCalled { get; private set; }
        public string? LastConfigPath { get; private set; }
        public List<PluginLoaderNotification> Notifications { get; } = [];

        public void OnNotification(PluginLoaderNotification notification)
            => Notifications.Add(notification);

        public void OnLoadStart(string configurationFilePath)
        {
            OnLoadStartCalled = true;
            LastConfigPath = configurationFilePath;
        }

        public void OnLoadCompleted(string configurationFilePath)
        {
            OnLoadCompletedCalled = true;
        }
    }

    private class ThrowingCallback : IPluginLoaderCallback
    {
        public void OnLoadStart(string configurationFilePath)
            => throw new InvalidOperationException("コールバック内で意図的に例外をスロー");
    }
}
