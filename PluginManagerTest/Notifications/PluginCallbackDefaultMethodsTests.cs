using PluginManager;
using PluginManager.Ipc;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// コールバックインターフェース既定実装のテストです。
/// </summary>
public sealed class PluginCallbackDefaultMethodsTests
{
    [Fact]
    public void IPluginProcessCallback_DefaultMethods_DoNotThrow()
    {
        IPluginProcessCallback callback = new EmptyProcessCallback();
        var notification = new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.HostStarted,
            Message = "start",
            ProcessId = 1,
        };

        var ex = Record.Exception(() =>
        {
            callback.OnNotification(notification);
            callback.OnHostStarted(1);
            callback.OnLoadCompleted("plugin-a");
            callback.OnLoadFailed("plugin-a", "error");
            callback.OnInitializeStarted("plugin-a");
            callback.OnInitializeCompleted("plugin-a");
            callback.OnInitializeFailed("plugin-a", "error");
            callback.OnExecuteStarted("plugin-a", "Processing");
            callback.OnExecuteCompleted("plugin-a", "Processing");
            callback.OnExecuteFailed("plugin-a", "Processing", "error");
            callback.OnUnloadCompleted("plugin-a");
            callback.OnUnloadFailed("plugin-a", "error");
            callback.OnShutdownReceived();
        });

        Assert.Null(ex);
    }

    [Fact]
    public void IPluginLoaderCallback_DefaultMethods_DoNotThrow()
    {
        IPluginLoaderCallback callback = new EmptyLoaderCallback();
        var notification = new PluginLoaderNotification(PluginLoaderNotificationType.LoadStart, "start", configurationFilePath: "config.json");

        var ex = Record.Exception(() =>
        {
            callback.OnNotification(notification);
            callback.OnLoadStart("config.json");
            callback.OnLoadCompleted("config.json");
            callback.OnPluginLoadStart("plugin-a", 1);
            callback.OnPluginLoadRetry("plugin-a", 2, new InvalidOperationException("retry"));
            callback.OnPluginLoadSuccess("plugin-a", 3);
            callback.OnPluginLoadFailed("plugin-a", 4, new InvalidOperationException("failed"));
            callback.OnExecuteStart("Processing");
            callback.OnExecuteCompleted("Processing");
            callback.OnExecuteFailed("Processing", new InvalidOperationException("error"));
        });

        Assert.Null(ex);
    }

    [Fact]
    public void IPluginExecutorCallback_DefaultMethods_DoNotThrow()
    {
        IPluginExecutorCallback callback = new EmptyExecutorCallback();
        var notification = new PluginExecutorNotification(
            PluginExecutorNotificationType.GroupStart,
            "start",
            "Processing",
            1,
            pluginId: "plugin-a",
            executionId: "exec-1",
            skipReason: "skip",
            exception: new InvalidOperationException("error"));

        var ex = Record.Exception(() =>
        {
            callback.OnNotification(notification);
            callback.OnGroupStart("Processing", 1);
            callback.OnGroupCompleted("Processing", 1);
            callback.OnPluginExecuteStart("Processing", "plugin-a", 1);
            callback.OnPluginExecuteCompleted("Processing", "plugin-a", 1);
            callback.OnPluginExecuteFailed("Processing", "plugin-a", 1, new InvalidOperationException("error"));
            callback.OnPluginSkipped("Processing", "plugin-a", 1, "skip");
        });

        Assert.Null(ex);
    }

    private sealed class EmptyProcessCallback : IPluginProcessCallback;

    private sealed class EmptyLoaderCallback : IPluginLoaderCallback;

    private sealed class EmptyExecutorCallback : IPluginExecutorCallback;
}
