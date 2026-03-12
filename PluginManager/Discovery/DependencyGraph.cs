namespace PluginManager;

/// <summary>
/// プラグインの依存関係を表すグラフ構造です。
/// </summary>
/// <remarks>
/// <b>グラフ構造:</b><br/>
/// - A が B に依存する場合: A.Dependencies に B を追加、B.Dependents に A を追加<br/>
/// - IncomingCount: このノードに依存している他のノードの数（実行前に処理が必要なノード数）
/// </remarks>
internal sealed class DependencyGraph
{
    private readonly Dictionary<string, DependencyNode> _nodes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// グラフにノードを追加します。
    /// </summary>
    /// <param name="pluginId">プラグイン ID。</param>
    /// <param name="descriptor">プラグイン記述子。</param>
    public void AddNode(string pluginId, PluginDescriptor descriptor)
    {
        if (!_nodes.ContainsKey(pluginId))
            _nodes[pluginId] = new DependencyNode(pluginId, descriptor);
    }

    /// <summary>
    /// ノード間に依存関係のエッジを追加します。
    /// </summary>
    /// <param name="fromPluginId">依存元のプラグイン ID（このプラグインが他に依存する）。</param>
    /// <param name="toPluginId">依存先のプラグイン ID（依存される側）。</param>
    /// <remarks>
    /// fromPluginId が toPluginId に依存している場合:
    /// - fromNode.Dependencies に toNode を追加
    /// - toNode.Dependents に fromNode を追加  
    /// - fromNode.IncomingCount++ (fromNode は toNode の後に実行される)
    /// </remarks>
    public void AddEdge(string fromPluginId, string toPluginId)
    {
        if (!_nodes.TryGetValue(fromPluginId, out var fromNode))
            throw new InvalidOperationException($"プラグイン '{fromPluginId}' が見つかりません。");

        if (!_nodes.TryGetValue(toPluginId, out var toNode))
            throw new InvalidOperationException($"依存先プラグイン '{toPluginId}' が見つかりません。");

        fromNode.Dependencies.Add(toNode);
        toNode.Dependents.Add(fromNode);
        fromNode.IncomingCount++;
    }

    /// <summary>
    /// すべてのノードを取得します。
    /// </summary>
    public IReadOnlyCollection<DependencyNode> Nodes => _nodes.Values;

    /// <summary>
    /// 指定されたプラグイン ID のノードを取得します。
    /// </summary>
    public DependencyNode? GetNode(string pluginId)
        => _nodes.TryGetValue(pluginId, out var node) ? node : null;
}

/// <summary>
/// 依存グラフのノードを表します。
/// </summary>
internal sealed class DependencyNode
{
    public DependencyNode(string pluginId, PluginDescriptor descriptor)
    {
        PluginId = pluginId;
        Descriptor = descriptor;
    }

    /// <summary>プラグイン ID。</summary>
    public string PluginId { get; }

    /// <summary>プラグイン記述子。</summary>
    public PluginDescriptor Descriptor { get; }

    /// <summary>このノードが依存する他のノードのリスト（このノードより先に実行が必要）。</summary>
    public List<DependencyNode> Dependencies { get; } = new();

    /// <summary>このノードに依存している他のノードのリスト（このノードの後に実行）。</summary>
    public List<DependencyNode> Dependents { get; } = new();

    /// <summary>このノードが依存しているノードの数（=Dependencies.Count）。</summary>
    public int IncomingCount { get; set; }

    /// <summary>トポロジカルソート用の一時的な入次数カウンター。</summary>
    public int TempIncomingCount { get; set; }

    public override string ToString() => $"{PluginId} (Deps={Dependencies.Count}, InDegree={IncomingCount})";
}
