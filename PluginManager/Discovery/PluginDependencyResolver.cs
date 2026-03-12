namespace PluginManager;

/// <summary>
/// プラグインの依存関係を解決し、実行順序を決定します。
/// </summary>
internal static class PluginDependencyResolver
{
    /// <summary>
    /// 依存関係と Order 設定を考慮してプラグインの実行順序を解決します。
    /// </summary>
    /// <param name="descriptors">プラグイン記述子のリスト。</param>
    /// <param name="dependencies">依存関係定義のリスト。</param>
    /// <param name="stageOrders">ステージごとの Order 定義。</param>
    /// <returns>実行順序で並べ替えられたプラグイン記述子のリスト。</returns>
    /// <exception cref="InvalidOperationException">循環依存が検出された場合。</exception>
    public static IReadOnlyList<PluginDescriptor> ResolveExecutionOrder(
        IReadOnlyList<PluginDescriptor> descriptors,
        IReadOnlyList<PluginDependencyEntry> dependencies,
        IReadOnlyList<PluginStageOrderEntry> stageOrders)
    {
        if (dependencies.Count == 0)
            return descriptors;

        var graph = BuildDependencyGraph(descriptors, dependencies);
        DetectCycles(graph);
        var sortedByDependency = TopologicalSort(graph);
        
        return MergeWithManualOrders(sortedByDependency, stageOrders);
    }

    /// <summary>
    /// 依存関係グラフを構築します。
    /// </summary>
    private static DependencyGraph BuildDependencyGraph(
        IReadOnlyList<PluginDescriptor> descriptors,
        IReadOnlyList<PluginDependencyEntry> dependencies)
    {
        var graph = new DependencyGraph();
        var descriptorLookup = descriptors.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in descriptors)
        {
            graph.AddNode(descriptor.Id, descriptor);
        }

        foreach (var dependency in dependencies)
        {
            if (!descriptorLookup.ContainsKey(dependency.Id))
                continue;

            foreach (var dependsOn in dependency.DependsOn)
            {
                if (!descriptorLookup.ContainsKey(dependsOn))
                {
                    throw new InvalidOperationException(
                        $"プラグイン '{dependency.Id}' が依存する '{dependsOn}' が見つかりません。");
                }

                graph.AddEdge(dependency.Id, dependsOn);
            }
        }

        return graph;
    }

    /// <summary>
    /// 循環依存を検出します（DFS ベース）。
    /// </summary>
    private static void DetectCycles(DependencyGraph graph)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recursionStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        foreach (var node in graph.Nodes)
        {
            if (!visited.Contains(node.PluginId))
            {
                if (DetectCyclesRecursive(node, visited, recursionStack, path))
                {
                    path.Add(path[0]);
                    throw new InvalidOperationException(
                        $"循環依存を検出しました: {string.Join(" → ", path)}");
                }
            }
        }
    }

    private static bool DetectCyclesRecursive(
        DependencyNode node,
        HashSet<string> visited,
        HashSet<string> recursionStack,
        List<string> path)
    {
        visited.Add(node.PluginId);
        recursionStack.Add(node.PluginId);
        path.Add(node.PluginId);

        foreach (var dependency in node.Dependencies)
        {
            if (!visited.Contains(dependency.PluginId))
            {
                if (DetectCyclesRecursive(dependency, visited, recursionStack, path))
                    return true;
            }
            else if (recursionStack.Contains(dependency.PluginId))
            {
                var cycleStartIndex = path.IndexOf(dependency.PluginId);
                path.RemoveRange(0, cycleStartIndex);
                return true;
            }
        }

        recursionStack.Remove(node.PluginId);
        path.RemoveAt(path.Count - 1);
        return false;
    }

    /// <summary>
    /// Kahn's algorithm によるトポロジカルソートを実行します。
    /// </summary>
    private static IReadOnlyList<PluginDescriptor> TopologicalSort(DependencyGraph graph)
    {
        var queue = new Queue<DependencyNode>();
        var sorted = new List<PluginDescriptor>();

        foreach (var node in graph.Nodes)
        {
            node.TempIncomingCount = node.IncomingCount;
            if (node.TempIncomingCount == 0)
                queue.Enqueue(node);
        }

        while (queue.Count > 0)
        {
            var currentGroup = new List<DependencyNode>();
            var groupSize = queue.Count;

            for (var i = 0; i < groupSize; i++)
            {
                currentGroup.Add(queue.Dequeue());
            }

            currentGroup.Sort((a, b) => string.Compare(a.Descriptor.Name, b.Descriptor.Name, StringComparison.OrdinalIgnoreCase));

            foreach (var node in currentGroup)
            {
                sorted.Add(node.Descriptor);

                // このノードに依存していた他のノード (Dependents) の入次数を減らす
                foreach (var dependent in node.Dependents)
                {
                    dependent.TempIncomingCount--;
                    if (dependent.TempIncomingCount == 0)
                        queue.Enqueue(dependent);
                }
            }
        }

        if (sorted.Count != graph.Nodes.Count)
        {
            var remaining = graph.Nodes.Where(n => n.TempIncomingCount > 0).Select(n => n.PluginId);
            throw new InvalidOperationException(
                $"依存関係の解決に失敗しました。未処理のプラグイン: {string.Join(", ", remaining)}");
        }

        return sorted;
    }

    /// <summary>
    /// トポロジカルソート結果と手動 Order を統合します。
    /// </summary>
    private static IReadOnlyList<PluginDescriptor> MergeWithManualOrders(
        IReadOnlyList<PluginDescriptor> sortedByDependency,
        IReadOnlyList<PluginStageOrderEntry> stageOrders)
    {
        if (stageOrders.Count == 0)
            return sortedByDependency;

        var manualOrderLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var stageOrder in stageOrders)
        {
            foreach (var pluginOrder in stageOrder.PluginOrder)
            {
                if (!manualOrderLookup.ContainsKey(pluginOrder.Id))
                    manualOrderLookup[pluginOrder.Id] = pluginOrder.Order;
            }
        }

        var autoOrder = 1000;
        return sortedByDependency
            .Select(d =>
            {
                var order = manualOrderLookup.TryGetValue(d.Id, out var manualOrder)
                    ? manualOrder
                    : autoOrder++;
                return (Descriptor: d, Order: order);
            })
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Descriptor)
            .ToList();
    }
}
