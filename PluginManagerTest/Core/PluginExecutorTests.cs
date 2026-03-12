using System.Collections.Frozen;
using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// PluginExecutor のユニットテストです。
/// </summary>
public sealed class PluginExecutorTests
{
    private static readonly PluginStage _stage = PluginStage.Processing;

    // ---------------------------------------------------------------
    // ExecutePluginsAndWaitAsync (フラット版)
    // ---------------------------------------------------------------

    [Fact]
    public async Task ExecutePluginsAndWaitAsync_EmptyList_ReturnsEmpty()
    {
        var context = new PluginContext();
        var result = await PluginExecutor.ExecutePluginsAndWaitAsync([], _stage, context);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ExecutePluginsAndWaitAsync_FailedLoad_ReturnsSkipped()
    {
        var descriptor = CreateDescriptor("plugin-a", _stage);
        var loadResult = new PluginLoadResult(descriptor, null, new InvalidOperationException("ロード失敗"));
        var context = new PluginContext();

        var results = await PluginExecutor.ExecutePluginsAndWaitAsync([loadResult], _stage, context);

        Assert.Single(results);
        Assert.True(results[0].Skipped);
    }

    [Fact]
    public async Task ExecutePluginsAndWaitAsync_UnsupportedStage_ReturnsSkipped()
    {
        var descriptor = CreateDescriptor("plugin-a", PluginStage.PreProcessing); // _stage とは異なるステージ
        var plugin = new OrderTrackingPlugin("plugin-a");
        var loadResult = new PluginLoadResult(descriptor, plugin);
        var context = new PluginContext();

        var results = await PluginExecutor.ExecutePluginsAndWaitAsync([loadResult], _stage, context);

        Assert.Single(results);
        Assert.True(results[0].Skipped);
        Assert.Empty(plugin.ExecutedOrder);
    }

    [Fact]
    public async Task ExecutePluginsAndWaitAsync_PluginThrows_ReturnsError()
    {
        var descriptor = CreateDescriptor("plugin-a", _stage);
        var plugin = new ThrowingPlugin();
        var loadResult = new PluginLoadResult(descriptor, plugin);
        var context = new PluginContext();

        var results = await PluginExecutor.ExecutePluginsAndWaitAsync([loadResult], _stage, context);

        Assert.Single(results);
        Assert.False(results[0].Success);
        Assert.NotNull(results[0].Error);
    }

    // ---------------------------------------------------------------
    // ExecutePluginsInGroupsAsync (グループ版)
    // ---------------------------------------------------------------

    [Fact]
    public async Task ExecutePluginsInGroupsAsync_EmptyGroups_ReturnsEmpty()
    {
        var context = new PluginContext();
        var result = await PluginExecutor.ExecutePluginsInGroupsAsync([], _stage, context);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ExecutePluginsInGroupsAsync_SingleGroup_ExecutesAllInParallel()
    {
        // 同一グループ内 → すべて並列実行
        var context = new PluginContext();
        var pluginA = new OrderTrackingPlugin("plugin-a");
        var pluginB = new OrderTrackingPlugin("plugin-b");

        var groups = BuildGroups(context,
            (1, [("plugin-a", pluginA), ("plugin-b", pluginB)]));

        var results = await PluginExecutor.ExecutePluginsInGroupsAsync(groups, _stage, context);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task ExecutePluginsInGroupsAsync_MultipleGroups_ExecutesGroupsSequentially()
    {
        // グループ 1 が完了してからグループ 2 が開始する（逐次）ことを検証
        var context = new PluginContext();
        var log = new List<string>();

        var pluginA = new LoggingPlugin("A", log, delayMs: 50);
        var pluginB = new LoggingPlugin("B", log, delayMs: 0);

        var groups = BuildGroups(context,
            (1, [("plugin-a", pluginA)]),
            (2, [("plugin-b", pluginB)]));

        var results = await PluginExecutor.ExecutePluginsInGroupsAsync(groups, _stage, context);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));

        // A（遅延あり）が B より先に完了している（A → B の順）
        Assert.Equal(["A", "B"], log);
    }

    [Fact]
    public async Task ExecutePluginsInGroupsAsync_ResultOrderFollowsGroupOrder()
    {
        // グループ 1 の結果がグループ 2 の結果より先に返る
        var context = new PluginContext();
        var pluginA = new OrderTrackingPlugin("plugin-a");
        var pluginB = new OrderTrackingPlugin("plugin-b");
        var pluginC = new OrderTrackingPlugin("plugin-c");

        var groups = BuildGroups(context,
            (1, [("plugin-a", pluginA)]),
            (2, [("plugin-b", pluginB), ("plugin-c", pluginC)]));

        var results = await PluginExecutor.ExecutePluginsInGroupsAsync(groups, _stage, context);

        Assert.Equal(3, results.Count);
        Assert.Equal("plugin-a", results[0].Descriptor.Id);
        Assert.Equal("plugin-b", results[1].Descriptor.Id);
        Assert.Equal("plugin-c", results[2].Descriptor.Id);
    }

    [Fact]
    public async Task ExecutePluginsInGroupsAsync_OneGroupFails_OtherGroupStillExecutes()
    {
        // グループ 1 内のプラグインが例外を投げても、グループ 2 は実行される
        var context = new PluginContext();
        var throwing = new ThrowingPlugin();
        var normal = new OrderTrackingPlugin("plugin-b");

        var groups = BuildGroups(context,
            (1, [("plugin-a", throwing)]),
            (2, [("plugin-b", normal)]));

        var results = await PluginExecutor.ExecutePluginsInGroupsAsync(groups, _stage, context);

        Assert.Equal(2, results.Count);
        Assert.False(results[0].Success);   // グループ 1 は失敗
        Assert.True(results[1].Success);    // グループ 2 は成功
    }

    [Fact]
    public async Task ExecutePluginsInGroupsAsync_CancellationRequested_StopsExecution()
    {
        var context = new PluginContext();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var plugin = new OrderTrackingPlugin("plugin-a");
        var groups = BuildGroups(context, (1, [("plugin-a", plugin)]));

        // キャンセル済み CT でも例外ではなく結果が返ること（ExecuteSafeAsync で catch 済み）
        var results = await PluginExecutor.ExecutePluginsInGroupsAsync(groups, _stage, context, cts.Token);
        Assert.Single(results);
    }

    [Fact]
    public async Task ExecutePluginsInGroupsAsync_WithMaxDegreeOfParallelism_LimitsConcurrency()
    {
        var context = new PluginContext();
        var concurrencyProbe = new ConcurrencyProbe();

        var groups = BuildGroups(context,
            (1,
                [
                    ("plugin-a", (IPlugin)new ConcurrencyTrackingPlugin("plugin-a", concurrencyProbe, 80)),
                    ("plugin-b", (IPlugin)new ConcurrencyTrackingPlugin("plugin-b", concurrencyProbe, 80)),
                    ("plugin-c", (IPlugin)new ConcurrencyTrackingPlugin("plugin-c", concurrencyProbe, 80)),
                ]));

        var results = await PluginExecutor.ExecutePluginsInGroupsAsync(
            groups,
            _stage,
            context,
            cancellationToken: default,
            maxDegreeOfParallelism: 1);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(1, concurrencyProbe.MaxObserved);
    }

    // ---------------------------------------------------------------
    // ヘルパー
    // ---------------------------------------------------------------

    private static PluginDescriptor CreateDescriptor(string id, PluginStage stage)
        => new(id, id, new Version(1, 0, 0), "TestPlugin", "test.dll",
               new[] { stage }.ToFrozenSet());

    /// <summary>
    /// Order グループ化済みの PluginLoadResult リストを構築します。
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<PluginLoadResult>> BuildGroups(
        PluginContext _,
        params (int Order, (string Id, IPlugin Plugin)[] Plugins)[] groupDefs)
        => groupDefs
            .OrderBy(g => g.Order)
            .Select(g => (IReadOnlyList<PluginLoadResult>)g.Plugins
                .Select(p => new PluginLoadResult(CreateDescriptor(p.Id, _stage), p.Plugin))
                .ToList())
            .ToList();

    // ---------------------------------------------------------------
    // テスト用プラグイン実装
    // ---------------------------------------------------------------

    /// <summary>実行順序を記録するプラグイン。</summary>
    private sealed class OrderTrackingPlugin(string id) : IPlugin
    {
        public string Id => id;
        public string Name => id;
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } =
            new[] { _stage }.ToFrozenSet();

        public List<int> ExecutedOrder { get; } = [];
        private static int _counter;

        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public async Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            ExecutedOrder.Add(Interlocked.Increment(ref _counter));
            return ExecutedOrder[^1];
        }
    }

    /// <summary>実行ログを記録するプラグイン（遅延付き）。</summary>
    private sealed class LoggingPlugin(string tag, List<string> log, int delayMs) : IPlugin
    {
        public string Id => tag;
        public string Name => tag;
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } =
            new[] { _stage }.ToFrozenSet();

        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public async Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
        {
            if (delayMs > 0)
                await Task.Delay(delayMs, cancellationToken);
            lock (log) log.Add(tag);
            return tag;
        }
    }

    /// <summary>常に例外をスローするプラグイン。</summary>
    private sealed class ThrowingPlugin : IPlugin
    {
        public string Id => "throwing";
        public string Name => "throwing";
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } =
            new[] { _stage }.ToFrozenSet();

        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("テスト例外");
    }

    private sealed class ConcurrencyProbe
    {
        private int _current;
        private int _maxObserved;

        public int MaxObserved => _maxObserved;

        public void Enter()
        {
            var current = Interlocked.Increment(ref _current);
            while (true)
            {
                var snapshot = Volatile.Read(ref _maxObserved);
                if (current <= snapshot)
                    return;
                if (Interlocked.CompareExchange(ref _maxObserved, current, snapshot) == snapshot)
                    return;
            }
        }

        public void Exit()
            => Interlocked.Decrement(ref _current);
    }

    private sealed class ConcurrencyTrackingPlugin(string id, ConcurrencyProbe probe, int delayMs) : IPlugin
    {
        public string Id => id;
        public string Name => id;
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } =
            new[] { _stage }.ToFrozenSet();

        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public async Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
        {
            probe.Enter();
            try
            {
                await Task.Delay(delayMs, cancellationToken);
                return id;
            }
            finally
            {
                probe.Exit();
            }
        }
    }
}
