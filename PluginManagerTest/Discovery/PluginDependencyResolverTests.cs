using System.Collections.Frozen;
using System.Reflection;
using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// プラグイン依存関係解決の統合テストです。
/// </summary>
public sealed class PluginDependencyResolverTests
{
    [Fact]
    public void OrderByConfiguration_WithSimpleDependency_ReturnsCorrectOrder()
    {
        var descriptors = new[]
        {
            CreateDescriptor("plugin-a"),
            CreateDescriptor("plugin-b"),
            CreateDescriptor("plugin-c"),
        };

        var dependencies = new[]
        {
            new PluginDependencyEntry { Id = "plugin-b", DependsOn = ["plugin-a"] },
            new PluginDependencyEntry { Id = "plugin-c", DependsOn = ["plugin-b"] },
        };

        var result = PluginOrderResolver.OrderByConfiguration(descriptors, [], dependencies);

        Assert.Equal(3, result.Count);
        Assert.Equal("plugin-a", result[0].Id);
        Assert.Equal("plugin-b", result[1].Id);
        Assert.Equal("plugin-c", result[2].Id);
    }

    [Fact]
    public void ResolveExecutionOrder_WithNoDependencies_ReturnsOriginalReference()
    {
        var descriptors = new[]
        {
            CreateDescriptor("plugin-a"),
            CreateDescriptor("plugin-b"),
        };

        var result = PluginDependencyResolver.ResolveExecutionOrder(descriptors, [], []);

        Assert.Same(descriptors, result);
    }

    [Fact]
    public void OrderByConfiguration_WithMultipleDependencies_ReturnsCorrectOrder()
    {
        var descriptors = new[]
        {
            CreateDescriptor("config"),
            CreateDescriptor("logger"),
            CreateDescriptor("database"),
            CreateDescriptor("cache"),
            CreateDescriptor("session"),
        };

        var dependencies = new[]
        {
            new PluginDependencyEntry { Id = "logger", DependsOn = ["config"] },
            new PluginDependencyEntry { Id = "database", DependsOn = ["config", "logger"] },
            new PluginDependencyEntry { Id = "cache", DependsOn = ["database"] },
            new PluginDependencyEntry { Id = "session", DependsOn = ["cache", "logger"] },
        };

        var result = PluginOrderResolver.OrderByConfiguration(descriptors, [], dependencies);

        Assert.Equal(5, result.Count);
        Assert.Equal("config", result[0].Id);
        Assert.Equal("logger", result[1].Id);
        Assert.Equal("database", result[2].Id);
        Assert.Equal("cache", result[3].Id);
        Assert.Equal("session", result[4].Id);
    }

    [Fact]
    public void ResolveExecutionOrder_WithDependencyEntryForUnknownPlugin_IgnoresEntry()
    {
        var descriptors = new[]
        {
            CreateDescriptor("plugin-a", "Plugin A"),
            CreateDescriptor("plugin-b", "Plugin B"),
        };

        var dependencies = new[]
        {
            new PluginDependencyEntry { Id = "missing-plugin", DependsOn = ["plugin-a"] },
        };

        var result = PluginDependencyResolver.ResolveExecutionOrder(descriptors, dependencies, []);

        Assert.Equal(["plugin-a", "plugin-b"], result.Select(x => x.Id).ToArray());
    }

    [Fact]
    public void OrderByConfiguration_WithDiamondDependency_ReturnsValidOrder()
    {
        var descriptors = new[]
        {
            CreateDescriptor("plugin-a"),
            CreateDescriptor("plugin-b"),
            CreateDescriptor("plugin-c"),
            CreateDescriptor("plugin-d"),
        };

        var dependencies = new[]
        {
            new PluginDependencyEntry { Id = "plugin-b", DependsOn = ["plugin-a"] },
            new PluginDependencyEntry { Id = "plugin-c", DependsOn = ["plugin-a"] },
            new PluginDependencyEntry { Id = "plugin-d", DependsOn = ["plugin-b", "plugin-c"] },
        };

        var result = PluginOrderResolver.OrderByConfiguration(descriptors, [], dependencies);

        Assert.Equal(4, result.Count);
        Assert.Equal("plugin-a", result[0].Id);
        Assert.True(result[1].Id == "plugin-b" || result[1].Id == "plugin-c");
        Assert.True(result[2].Id == "plugin-b" || result[2].Id == "plugin-c");
        Assert.Equal("plugin-d", result[3].Id);
    }

    [Fact]
    public void OrderByConfiguration_WithCircularDependency_ThrowsException()
    {
        var descriptors = new[]
        {
            CreateDescriptor("plugin-a"),
            CreateDescriptor("plugin-b"),
            CreateDescriptor("plugin-c"),
        };

        var dependencies = new[]
        {
            new PluginDependencyEntry { Id = "plugin-a", DependsOn = ["plugin-b"] },
            new PluginDependencyEntry { Id = "plugin-b", DependsOn = ["plugin-c"] },
            new PluginDependencyEntry { Id = "plugin-c", DependsOn = ["plugin-a"] },
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PluginOrderResolver.OrderByConfiguration(descriptors, [], dependencies));

        Assert.Contains("循環依存", ex.Message);
        Assert.Contains("plugin-a", ex.Message);
        Assert.Contains("plugin-b", ex.Message);
        Assert.Contains("plugin-c", ex.Message);
    }

    [Fact]
    public void OrderByConfiguration_WithSelfDependency_ThrowsException()
    {
        var descriptors = new[]
        {
            CreateDescriptor("plugin-a"),
        };

        var dependencies = new[]
        {
            new PluginDependencyEntry { Id = "plugin-a", DependsOn = ["plugin-a"] },
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PluginOrderResolver.OrderByConfiguration(descriptors, [], dependencies));

        Assert.Contains("循環依存", ex.Message);
    }

    [Fact]
    public void OrderByConfiguration_WithMissingDependency_ThrowsException()
    {
        var descriptors = new[]
        {
            CreateDescriptor("plugin-a"),
        };

        var dependencies = new[]
        {
            new PluginDependencyEntry { Id = "plugin-a", DependsOn = ["non-existent"] },
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PluginOrderResolver.OrderByConfiguration(descriptors, [], dependencies));

        Assert.Contains("non-existent", ex.Message);
        Assert.Contains("見つかりません", ex.Message);
    }

    [Fact]
    public void OrderByConfiguration_WithManualOrder_PrioritizesManualOrder()
    {
        var descriptors = new[]
        {
            CreateDescriptor("plugin-a"),
            CreateDescriptor("plugin-b"),
            CreateDescriptor("plugin-c"),
        };

        var dependencies = new[]
        {
            new PluginDependencyEntry { Id = "plugin-b", DependsOn = ["plugin-a"] },
            new PluginDependencyEntry { Id = "plugin-c", DependsOn = ["plugin-b"] },
        };

        var stageOrders = new[]
        {
            new PluginStageOrderEntry
            {
                Stage = PluginStage.Processing,
                PluginOrder =
                [
                    new PluginOrderEntry { Id = "plugin-c", Order = 1 },
                    new PluginOrderEntry { Id = "plugin-a", Order = 100 },
                ]
            }
        };

        var result = PluginOrderResolver.OrderByConfiguration(descriptors, stageOrders, dependencies);

        Assert.Equal(3, result.Count);
        Assert.Equal("plugin-c", result[0].Id);
        Assert.Equal("plugin-a", result[1].Id);
        Assert.Equal("plugin-b", result[2].Id);
    }

    [Fact]
    public void TopologicalSort_WithInconsistentIncomingCount_ThrowsInvalidOperationException()
    {
        var graph = new DependencyGraph();
        var descriptor = CreateDescriptor("plugin-a");
        graph.AddNode("plugin-a", descriptor);
        graph.GetNode("plugin-a")!.IncomingCount = 1;

        var method = typeof(PluginDependencyResolver).GetMethod("TopologicalSort", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var ex = Assert.Throws<TargetInvocationException>(() => method!.Invoke(null, [graph]));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("未処理のプラグイン", ex.InnerException!.Message);
        Assert.Contains("plugin-a", ex.InnerException.Message);
    }

    [Fact]
    public void OrderByConfiguration_WithNoDependencies_ReturnsOriginalOrder()
    {
        var descriptors = new[]
        {
            CreateDescriptor("plugin-a"),
            CreateDescriptor("plugin-b"),
        };

        var result = PluginOrderResolver.OrderByConfiguration(descriptors, [], []);

        Assert.Equal(2, result.Count);
    }

    private static PluginDescriptor CreateDescriptor(string id)
        => CreateDescriptor(id, id);

    private static PluginDescriptor CreateDescriptor(string id, string name)
    {
        return new PluginDescriptor(
            id,
            name,
            new Version(1, 0, 0),
            typeof(object).FullName!,
            $"{id}.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());
    }
}
