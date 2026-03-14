using System.Collections.Frozen;
using PluginHost;
using PluginManager;
using PluginManager.Ipc;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="PluginRegistry"/> のロード・アンロード分岐テストです。
/// </summary>
public sealed class PluginRegistryTests
{
    [Fact]
    public void Load_RequiredValuesMissing_ReturnsArgumentError()
    {
        var registry = CreateRegistry();

        var response = registry.Load(new PluginHostRequest
        {
            RequestId = "req-load-missing",
            Command = PluginHostCommand.Load,
            PluginId = "plugin-a",
        }, instanceIndex: 0);

        Assert.False(response.Success);
        Assert.Equal(nameof(ArgumentException), response.ErrorType);
        Assert.Contains("必要", response.ErrorMessage);
    }

    [Fact]
    public void Load_TypeNotFound_ReturnsInvalidOperationError()
    {
        var registry = CreateRegistry();

        var response = registry.Load(new PluginHostRequest
        {
            RequestId = "req-load-typenotfound",
            Command = PluginHostCommand.Load,
            PluginId = "plugin-a",
            AssemblyPath = typeof(PluginRegistryTests).Assembly.Location,
            PluginTypeName = "PluginManagerTest.NotFoundType",
        }, instanceIndex: 0);

        Assert.False(response.Success);
        Assert.Equal(nameof(InvalidOperationException), response.ErrorType);
        Assert.Contains("見つからない", response.ErrorMessage);
    }

    [Fact]
    public void Load_DuplicatePluginId_ReturnsInvalidOperationError()
    {
        var registry = CreateRegistry();
        var request = CreateLoadRequest("plugin-dup");

        var first = registry.Load(request, instanceIndex: 0);
        var second = registry.Load(request, instanceIndex: 1);

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal(nameof(InvalidOperationException), second.ErrorType);
        Assert.Contains("既にロード", second.ErrorMessage);

        registry.Unload(new PluginHostRequest
        {
            RequestId = "req-cleanup",
            Command = PluginHostCommand.Unload,
            PluginId = "plugin-dup",
        }, instanceIndex: 0);
    }

    [Fact]
    public void TryGet_AfterLoadAndUnload_ReturnsExpectedState()
    {
        var registry = CreateRegistry();

        var load = registry.Load(CreateLoadRequest("plugin-state"), instanceIndex: 0);
        var existsAfterLoad = registry.TryGet("plugin-state", out _);

        var unload = registry.Unload(new PluginHostRequest
        {
            RequestId = "req-unload-state",
            Command = PluginHostCommand.Unload,
            PluginId = "plugin-state",
        }, instanceIndex: 0);
        var existsAfterUnload = registry.TryGet("plugin-state", out _);

        Assert.True(load.Success);
        Assert.True(existsAfterLoad);
        Assert.True(unload.Success);
        Assert.False(existsAfterUnload);
    }

    [Fact]
    public void Unload_MissingPluginId_ReturnsArgumentError()
    {
        var registry = CreateRegistry();

        var response = registry.Unload(new PluginHostRequest
        {
            RequestId = "req-unload-missing",
            Command = PluginHostCommand.Unload,
        }, instanceIndex: 0);

        Assert.False(response.Success);
        Assert.Equal(nameof(ArgumentException), response.ErrorType);
        Assert.Contains("PluginId が必要", response.ErrorMessage);
    }

    [Fact]
    public void Unload_NotFound_ReturnsInvalidOperationError()
    {
        var registry = CreateRegistry();

        var response = registry.Unload(new PluginHostRequest
        {
            RequestId = "req-unload-notfound",
            Command = PluginHostCommand.Unload,
            PluginId = "plugin-none",
        }, instanceIndex: 0);

        Assert.False(response.Success);
        Assert.Equal(nameof(InvalidOperationException), response.ErrorType);
        Assert.Contains("見つかりません", response.ErrorMessage);
    }

    [Fact]
    public void UnloadAll_PublishesStartAndCompletedNotifications()
    {
        var mapName = $"PluginRegistryTests_{Guid.NewGuid():N}";
        using var writer = new MemoryMappedNotificationQueue(mapName);
        using var reader = new MemoryMappedNotificationQueue(mapName);

        var notifier = new PluginHostNotifier(writer);
        using var registry = new PluginRegistry(notifier);

        registry.UnloadAll();

        var notifications = reader.Drain();
        Assert.Contains(notifications, n => n.NotificationType == PluginProcessNotificationType.UnloadAllStarted);
        Assert.Contains(notifications, n => n.NotificationType == PluginProcessNotificationType.UnloadAllCompleted);
    }

    private static PluginRegistry CreateRegistry()
        => new(new PluginHostNotifier());

    private static PluginHostRequest CreateLoadRequest(string pluginId)
        => new()
        {
            RequestId = $"req-load-{pluginId}",
            Command = PluginHostCommand.Load,
            PluginId = pluginId,
            AssemblyPath = CreateIsolatedPluginAssemblyPath(),
            PluginTypeName = typeof(RegistryLoadablePlugin).FullName!,
        };

    private static string CreateIsolatedPluginAssemblyPath()
    {
        var sourceAssemblyPath = typeof(PluginRegistryTests).Assembly.Location;
        var tempDirectory = Path.Combine(Path.GetTempPath(), "PluginManagerTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        var copiedAssemblyPath = Path.Combine(tempDirectory, Path.GetFileName(sourceAssemblyPath));
        File.Copy(sourceAssemblyPath, copiedAssemblyPath, overwrite: true);
        return copiedAssemblyPath;
    }

    public sealed class RegistryLoadablePlugin : IPlugin
    {
        public string Id => "registry-loadable";
        public string Name => "RegistryLoadable";
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } =
            new[] { PluginStage.Processing }.ToFrozenSet();

        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
            => Task.FromResult<object?>("ok");
    }
}
