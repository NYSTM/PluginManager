using System.Collections.Frozen;
using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="DependencyGraph"/> のテストです。
/// </summary>
public sealed class DependencyGraphTests
{
    [Fact]
    public void AddNode_FirstTime_AddsNode()
    {
        var graph = new DependencyGraph();
        var descriptor = CreateDescriptor("plugin-a");

        graph.AddNode("plugin-a", descriptor);

        var node = graph.GetNode("plugin-a");
        Assert.NotNull(node);
        Assert.Equal("plugin-a", node!.PluginId);
        Assert.Same(descriptor, node.Descriptor);
    }

    [Fact]
    public void AddNode_SameIdDifferentCase_DoesNotDuplicate()
    {
        var graph = new DependencyGraph();

        graph.AddNode("plugin-a", CreateDescriptor("plugin-a"));
        graph.AddNode("PLUGIN-A", CreateDescriptor("PLUGIN-A"));

        Assert.Single(graph.Nodes);
    }

    [Fact]
    public void AddEdge_ExistingNodes_UpdatesDependenciesAndDependents()
    {
        var graph = new DependencyGraph();
        graph.AddNode("plugin-a", CreateDescriptor("plugin-a"));
        graph.AddNode("plugin-b", CreateDescriptor("plugin-b"));

        graph.AddEdge("plugin-a", "plugin-b");

        var fromNode = graph.GetNode("plugin-a")!;
        var toNode = graph.GetNode("plugin-b")!;
        Assert.Single(fromNode.Dependencies);
        Assert.Single(toNode.Dependents);
        Assert.Same(toNode, fromNode.Dependencies[0]);
        Assert.Same(fromNode, toNode.Dependents[0]);
        Assert.Equal(1, fromNode.IncomingCount);
    }

    [Fact]
    public void AddEdge_MissingFromNode_ThrowsInvalidOperationException()
    {
        var graph = new DependencyGraph();
        graph.AddNode("plugin-b", CreateDescriptor("plugin-b"));

        var ex = Assert.Throws<InvalidOperationException>(() => graph.AddEdge("plugin-a", "plugin-b"));
        Assert.Contains("plugin-a", ex.Message);
    }

    [Fact]
    public void AddEdge_MissingToNode_ThrowsInvalidOperationException()
    {
        var graph = new DependencyGraph();
        graph.AddNode("plugin-a", CreateDescriptor("plugin-a"));

        var ex = Assert.Throws<InvalidOperationException>(() => graph.AddEdge("plugin-a", "plugin-b"));
        Assert.Contains("plugin-b", ex.Message);
    }

    [Fact]
    public void GetNode_DifferentCase_ReturnsNode()
    {
        var graph = new DependencyGraph();
        graph.AddNode("plugin-a", CreateDescriptor("plugin-a"));

        var node = graph.GetNode("PLUGIN-A");

        Assert.NotNull(node);
        Assert.Equal("plugin-a", node!.PluginId);
    }

    [Fact]
    public void GetNode_MissingNode_ReturnsNull()
    {
        var graph = new DependencyGraph();

        var node = graph.GetNode("missing-plugin");

        Assert.Null(node);
    }

    [Fact]
    public void DependencyNode_ToString_IncludesCounts()
    {
        var node = new DependencyNode("plugin-a", CreateDescriptor("plugin-a"))
        {
            IncomingCount = 2,
        };
        node.Dependencies.Add(new DependencyNode("plugin-b", CreateDescriptor("plugin-b")));

        var text = node.ToString();

        Assert.Contains("plugin-a", text);
        Assert.Contains("Deps=1", text);
        Assert.Contains("InDegree=2", text);
    }

    private static PluginDescriptor CreateDescriptor(string id)
        => new(
            id,
            id,
            new Version(1, 0, 0),
            typeof(object).FullName!,
            $"{id}.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());
}
