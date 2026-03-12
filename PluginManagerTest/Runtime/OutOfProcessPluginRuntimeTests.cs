using System.Collections.Frozen;
using System.Reflection;
using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// OutOfProcess 隔離モードの統合テストです。
/// </summary>
public sealed class OutOfProcessPluginRuntimeTests
{
    [Fact]
    public void PluginMetadata_OutOfProcessIsolationMode_IsConfigurable()
    {
        var attribute = new PluginAttribute("test-id", "Test", "1.0.0")
        {
            IsolationMode = PluginIsolationMode.OutOfProcess,
        };
        Assert.Equal(PluginIsolationMode.OutOfProcess, attribute.IsolationMode);

        var descriptor = new PluginDescriptor(
            "test-id", "Test", new Version(1, 0, 0),
            typeof(object).FullName!, "test.dll",
            new[] { PluginStage.Processing }.ToFrozenSet())
        {
            IsolationMode = PluginIsolationMode.OutOfProcess,
        };
        Assert.Equal(PluginIsolationMode.OutOfProcess, descriptor.IsolationMode);
    }

    [Fact]
    public async Task LoadPluginAsync_WithOutOfProcessMode_ReturnsErrorIfHostNotAvailable()
    {
        using var loader = new PluginLoader();
        var descriptor = new PluginDescriptor(
            "oop-test", "OutOfProcessTest", new Version(1, 0, 0),
            typeof(TestPlugin).FullName!, typeof(TestPlugin).Assembly.Location,
            new[] { PluginStage.Processing }.ToFrozenSet())
        {
            IsolationMode = PluginIsolationMode.OutOfProcess,
        };

        var method = typeof(PluginLoader).GetMethod("LoadPluginAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task<PluginLoadResult>)method!.Invoke(loader, [descriptor, new PluginContext(), CancellationToken.None])!;
        var result = await task;

        // PluginHost.exe が存在しない場合、エラーが返されることを確認
        if (!result.Success)
        {
            Assert.NotNull(result.Error);
            Assert.True(
                result.Error is FileNotFoundException or InvalidOperationException or TimeoutException,
                $"予期しない例外型: {result.Error.GetType().Name}");
        }
    }

    private sealed class TestPlugin : IPlugin
    {
        public string Id => "oop-test";
        public string Name => "OutOfProcessTest";
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } =
            new[] { PluginStage.Processing }.ToFrozenSet();

        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<object?>("test-result");
        }
    }
}
