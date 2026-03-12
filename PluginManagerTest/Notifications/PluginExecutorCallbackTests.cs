using System.Collections.Frozen;
using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="IPluginExecutorCallback"/> のテストです。
/// </summary>
public sealed class PluginExecutorCallbackTests
{
    private static readonly PluginStage _stage = PluginStage.Processing;

    [Fact]
    public async Task ExecutePluginsInGroupsAsync_PublishesGroupAndPluginNotifications()
    {
        var callback = new TestCallback();

        var descriptor = CreateDescriptor("plugin-a", _stage);
        var loadResult = new PluginLoadResult(descriptor, new SuccessPlugin());
        var groups = new[] { (IReadOnlyList<PluginLoadResult>)[loadResult] };

        var results = await PluginExecutor.ExecutePluginsInGroupsAsync(
            groups,
            _stage,
            new PluginContext(),
            callback: callback,
            executionId: "exec-1");

        Assert.Single(results);
        Assert.Collection(
            callback.Notifications,
            n => Assert.Equal(PluginExecutorNotificationType.GroupStart, n.NotificationType),
            n => Assert.Equal(PluginExecutorNotificationType.PluginExecuteStart, n.NotificationType),
            n => Assert.Equal(PluginExecutorNotificationType.PluginExecuteCompleted, n.NotificationType),
            n => Assert.Equal(PluginExecutorNotificationType.GroupCompleted, n.NotificationType));
        Assert.All(callback.Notifications, n => Assert.Equal("exec-1", n.ExecutionId));
        Assert.Equal(("Processing", 1), callback.LastGroupStart);
        Assert.Equal(("Processing", "plugin-a", 1), callback.LastPluginCompleted);
    }

    [Fact]
    public async Task ExecutePluginsAndWaitAsync_PublishesSkippedAndFailedNotifications()
    {
        var callback = new TestCallback();

        var skipped = new PluginLoadResult(CreateDescriptor("plugin-skip", PluginStage.PreProcessing), new SuccessPlugin());
        var failed = new PluginLoadResult(CreateDescriptor("plugin-fail", _stage), new ThrowingPlugin());

        var results = await PluginExecutor.ExecutePluginsAndWaitAsync(
            [skipped, failed],
            _stage,
            new PluginContext(),
            callback: callback,
            executionId: "exec-2");

        Assert.Equal(2, results.Count);
        Assert.Contains(callback.Notifications, n => n.NotificationType == PluginExecutorNotificationType.PluginSkipped && n.PluginId == "plugin-skip");
        Assert.Contains(callback.Notifications, n => n.NotificationType == PluginExecutorNotificationType.PluginExecuteFailed && n.PluginId == "plugin-fail");
        Assert.Equal(("Processing", "plugin-skip", 1, "ステージ 'Processing' は対象外です。"), callback.LastSkipped);
        Assert.Equal(("Processing", "plugin-fail", 1), callback.LastPluginFailed);
    }

    private static PluginDescriptor CreateDescriptor(string id, PluginStage stage)
        => new(id, id, new Version(1, 0, 0), "TestPlugin", "test.dll", new[] { stage }.ToFrozenSet());

    private sealed class TestCallback : IPluginExecutorCallback
    {
        public List<PluginExecutorNotification> Notifications { get; } = [];
        public (string StageId, int GroupIndex)? LastGroupStart { get; private set; }
        public (string StageId, string PluginId, int GroupIndex)? LastPluginCompleted { get; private set; }
        public (string StageId, string PluginId, int GroupIndex)? LastPluginFailed { get; private set; }
        public (string StageId, string PluginId, int GroupIndex, string SkipReason)? LastSkipped { get; private set; }

        public void OnNotification(PluginExecutorNotification notification)
            => Notifications.Add(notification);

        public void OnGroupStart(string stageId, int groupIndex)
            => LastGroupStart = (stageId, groupIndex);

        public void OnPluginExecuteCompleted(string stageId, string pluginId, int groupIndex)
            => LastPluginCompleted = (stageId, pluginId, groupIndex);

        public void OnPluginExecuteFailed(string stageId, string pluginId, int groupIndex, Exception error)
            => LastPluginFailed = (stageId, pluginId, groupIndex);

        public void OnPluginSkipped(string stageId, string pluginId, int groupIndex, string skipReason)
            => LastSkipped = (stageId, pluginId, groupIndex, skipReason);
    }

    private sealed class SuccessPlugin : IPlugin
    {
        public string Id => "success";
        public string Name => "success";
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } = new[] { _stage }.ToFrozenSet();

        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
            => Task.FromResult<object?>("ok");
    }

    private sealed class ThrowingPlugin : IPlugin
    {
        public string Id => "throw";
        public string Name => "throw";
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } = new[] { _stage }.ToFrozenSet();

        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("実行失敗");
    }
}
