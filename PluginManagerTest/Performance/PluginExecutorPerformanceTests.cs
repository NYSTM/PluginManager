using System.Collections.Frozen;
using System.Diagnostics;
using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="PluginExecutor"/> の高並列時スループット監視テストです。
/// </summary>
public sealed class PluginExecutorPerformanceTests
{
    private static readonly PluginStage _stage = PluginStage.Processing;

    [Fact]
    public async Task ExecutePluginsInGroupsAsync_WithHigherParallelism_IsFasterThanSerial()
    {
        var serialContext = new PluginContext();
        var parallelContext = new PluginContext();
        var serialGroups = BuildGroup(pluginCount: 8, delayMs: 80);
        var parallelGroups = BuildGroup(pluginCount: 8, delayMs: 80);

        var serialWatch = Stopwatch.StartNew();
        var serialResults = await PluginExecutor.ExecutePluginsInGroupsAsync(
            serialGroups,
            _stage,
            serialContext,
            maxDegreeOfParallelism: 1);
        serialWatch.Stop();

        var parallelWatch = Stopwatch.StartNew();
        var parallelResults = await PluginExecutor.ExecutePluginsInGroupsAsync(
            parallelGroups,
            _stage,
            parallelContext,
            maxDegreeOfParallelism: 4);
        parallelWatch.Stop();

        Assert.Equal(8, serialResults.Count);
        Assert.Equal(8, parallelResults.Count);
        Assert.All(serialResults, result => Assert.True(result.Success));
        Assert.All(parallelResults, result => Assert.True(result.Success));
        Assert.True(
            parallelWatch.ElapsedMilliseconds < serialWatch.ElapsedMilliseconds,
            $"高並列実行が逐次実行より速くありません。serial={serialWatch.ElapsedMilliseconds}ms parallel={parallelWatch.ElapsedMilliseconds}ms");
    }

    private static IReadOnlyList<IReadOnlyList<PluginLoadResult>> BuildGroup(int pluginCount, int delayMs)
        =>
        [
            Enumerable.Range(1, pluginCount)
                .Select(index => new PluginLoadResult(CreateDescriptor($"plugin-{index}"), new DelayedPlugin($"plugin-{index}", delayMs)))
                .ToList()
        ];

    private static PluginDescriptor CreateDescriptor(string id)
        => new(id, id, new Version(1, 0, 0), id, $"{id}.dll", new[] { _stage }.ToFrozenSet());

    private sealed class DelayedPlugin(string id, int delayMs) : IPlugin
    {
        public string Id => id;
        public string Name => id;
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } = new[] { _stage }.ToFrozenSet();
        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
        {
            await Task.Delay(delayMs, cancellationToken);
            return id;
        }
    }
}
