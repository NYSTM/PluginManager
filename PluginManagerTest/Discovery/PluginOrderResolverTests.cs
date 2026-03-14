using System.Collections.Frozen;
using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="PluginOrderResolver"/> のステージ順序解決テストです。
/// </summary>
public sealed class PluginOrderResolverTests
{
    [Fact]
    public void OrderByConfiguration_WithStageOrders_AggregatesStagesAndSortsByOrder()
    {
        var descriptors = new[]
        {
            CreateDescriptor("plugin-a", "PluginA"),
            CreateDescriptor("plugin-b", "PluginB"),
            CreateDescriptor("plugin-c", "PluginC"),
        };

        var stageOrders = new[]
        {
            new PluginStageOrderEntry
            {
                Stage = PluginStage.PreProcessing,
                PluginOrder =
                [
                    new PluginOrderEntry { Id = "PLUGIN-B", Order = 2 },
                    new PluginOrderEntry { Id = "   ", Order = 0 },
                ]
            },
            new PluginStageOrderEntry
            {
                Stage = PluginStage.Processing,
                PluginOrder =
                [
                    new PluginOrderEntry { Id = "plugin-a", Order = 1 },
                    new PluginOrderEntry { Id = "plugin-b", Order = 2 },
                ]
            },
            new PluginStageOrderEntry
            {
                Stage = null,
                PluginOrder =
                [
                    new PluginOrderEntry { Id = "plugin-c", Order = 0 },
                ]
            },
        };

        var result = PluginOrderResolver.OrderByConfiguration(descriptors, stageOrders, []);

        Assert.Equal(["plugin-a", "plugin-b", "plugin-c"], result.Select(x => x.Id).ToArray());
        Assert.Equal([PluginStage.PreProcessing, PluginStage.Processing], result[1].SupportedStages.OrderBy(x => x.Id).ToArray());
        Assert.Equal([PluginStage.Processing], result[0].SupportedStages.ToArray());
        Assert.Equal([PluginStage.Processing], result[2].SupportedStages.ToArray());
    }

    [Fact]
    public void BuildExecutionGroups_WithStageOrders_GroupsByOrderAndSortsByName()
    {
        var descriptors = new[]
        {
            CreateDescriptor("plugin-z", "Zeta"),
            CreateDescriptor("plugin-a", "Alpha"),
            CreateDescriptor("plugin-g", "Gamma"),
        };

        var stageOrders = new[]
        {
            new PluginStageOrderEntry
            {
                Stage = PluginStage.Processing,
                PluginOrder =
                [
                    new PluginOrderEntry { Id = "plugin-z", Order = 1 },
                    new PluginOrderEntry { Id = "plugin-a", Order = 1 },
                    new PluginOrderEntry { Id = "plugin-g", Order = 2 },
                ]
            },
        };

        var groups = PluginOrderResolver.BuildExecutionGroups(descriptors, stageOrders, []);

        Assert.Equal(2, groups.Count);
        Assert.Equal(["plugin-a", "plugin-z"], groups[0].Select(x => x.Id).ToArray());
        Assert.Equal(["plugin-g"], groups[1].Select(x => x.Id).ToArray());
    }

    [Fact]
    public void BuildExecutionGroups_WithoutConfiguration_ReturnsSingleGroup()
    {
        var descriptors = new[]
        {
            CreateDescriptor("plugin-a", "PluginA"),
            CreateDescriptor("plugin-b", "PluginB"),
        };

        var groups = PluginOrderResolver.BuildExecutionGroups(descriptors, [], []);

        Assert.Single(groups);
        Assert.Equal(["plugin-a", "plugin-b"], groups[0].Select(x => x.Id).ToArray());
    }

    private static PluginDescriptor CreateDescriptor(string id, string name)
        => new(
            id,
            name,
            new Version(1, 0, 0),
            typeof(object).FullName!,
            $"{id}.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());
}
