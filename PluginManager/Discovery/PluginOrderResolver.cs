using System.Collections.Frozen;

namespace PluginManager;

/// <summary>
/// 設定に基づきプラグインの実行順序を解決します。
/// </summary>
/// <remarks>
/// <para>
/// <b>Order ベースと依存関係ベースの統合:</b><br/>
/// - <see cref="PluginConfiguration.PluginDependencies"/> が指定されている場合、依存関係を考慮した実行順序を決定します。<br/>
/// - <see cref="PluginConfiguration.StageOrders"/> で明示的に指定された Order は優先されます。<br/>
/// - 循環依存が検出された場合、起動時に例外をスローします。
/// </para>
/// </remarks>
internal static class PluginOrderResolver
{
    /// <summary>
    /// 設定順序と依存関係に従いプラグインを並べ替えます。
    /// </summary>
    /// <param name="descriptors">探索済みプラグイン記述子の一覧。</param>
    /// <param name="stageOrders">設定ファイルから読み込んだステージ順序定義。</param>
    /// <param name="dependencies">プラグイン間の依存関係定義。</param>
    /// <returns>Order 昇順・同順位は名前昇順で並んだ <see cref="PluginDescriptor"/> の一覧。</returns>
    public static IReadOnlyList<PluginDescriptor> OrderByConfiguration(
        IReadOnlyList<PluginDescriptor> descriptors,
        IReadOnlyList<PluginStageOrderEntry> stageOrders,
        IReadOnlyList<PluginDependencyEntry> dependencies)
    {
        if (dependencies.Count > 0)
        {
            return PluginDependencyResolver.ResolveExecutionOrder(descriptors, dependencies, stageOrders);
        }

        if (stageOrders.Count == 0)
            return descriptors;

        var stageLookup = BuildStageLookup(stageOrders);

        return descriptors
            .Select(d =>
            {
                var (stages, order) = ResolveStagedOrder(d, stageLookup);
                return (Descriptor: stages is not null ? d with { SupportedStages = stages } : d, Order: order);
            })
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Descriptor)
            .ToList();
    }

    /// <summary>
    /// 設定順序と依存関係からプラグインの並列実行グループを構築します。
    /// </summary>
    /// <param name="descriptors">探索済みプラグイン記述子の一覧。</param>
    /// <param name="stageOrders">設定ファイルから読み込んだステージ順序定義。</param>
    /// <param name="dependencies">プラグイン間の依存関係定義。</param>
    /// <returns>Order 昇順にグループ化された <see cref="PluginDescriptor"/> のリスト。各グループ内は名前昇順。</returns>
    public static IReadOnlyList<IReadOnlyList<PluginDescriptor>> BuildExecutionGroups(
        IReadOnlyList<PluginDescriptor> descriptors,
        IReadOnlyList<PluginStageOrderEntry> stageOrders,
        IReadOnlyList<PluginDependencyEntry> dependencies)
    {
        var ordered = OrderByConfiguration(descriptors, stageOrders, dependencies);

        if (stageOrders.Count == 0 && dependencies.Count == 0)
            return [ordered];

        var stageLookup = BuildStageLookup(stageOrders);

        return ordered
            .Select(d =>
            {
                var (stages, order) = ResolveStagedOrder(d, stageLookup);
                return (Descriptor: stages is not null ? d with { SupportedStages = stages } : d, Order: order);
            })
            .GroupBy(x => x.Order)
            .OrderBy(g => g.Key)
            .Select(g => (IReadOnlyList<PluginDescriptor>)g
                .Select(x => x.Descriptor)
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList())
            .ToList();
    }

    /// <summary>
    /// ステージ順序設定から「プラグインID → (ステージセット, Order)」の高速ルックアップを構築します。
    /// 同一プラグイン ID が複数ステージに登録された場合、すべてのステージを集約します。
    /// 戻り値は <see cref="FrozenDictionary{TKey, TValue}"/> で不変かつ高速な読み取りを保証します。
    /// </summary>
    private static FrozenDictionary<string, (IReadOnlySet<PluginStage> Stages, int Order)> BuildStageLookup(
        IReadOnlyList<PluginStageOrderEntry> stageOrders)
    {
        // 中間バッファ: ステージを List で蓄積
        var buffer = new Dictionary<string, (List<PluginStage> Stages, int Order)>(StringComparer.OrdinalIgnoreCase);

        foreach (var stageEntry in stageOrders)
        {
            if (stageEntry.Stage is null)
                continue;

            var stage = stageEntry.Stage;
            foreach (var entry in stageEntry.PluginOrder)
            {
                if (string.IsNullOrWhiteSpace(entry.Id))
                    continue;

                if (buffer.TryGetValue(entry.Id, out var existing))
                    existing.Stages.Add(stage);
                else
                    buffer[entry.Id] = ([stage], entry.Order);
            }
        }

        // FrozenDictionary に変換（読み取り専用かつ高速なルックアップ）
        return buffer.ToFrozenDictionary(
            kv => kv.Key,
            kv => ((IReadOnlySet<PluginStage>)kv.Value.Stages.ToFrozenSet(), kv.Value.Order),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// プラグイン記述子に対応するステージセットと Order をルックアップから解決します。
    /// 設定に存在しない場合は <see langword="null"/> と <see cref="int.MaxValue"/> を返します。
    /// </summary>
    private static (IReadOnlySet<PluginStage>? Stages, int Order) ResolveStagedOrder(
        PluginDescriptor descriptor,
        FrozenDictionary<string, (IReadOnlySet<PluginStage> Stages, int Order)> stageLookup)
        => stageLookup.TryGetValue(descriptor.Id, out var value)
            ? (value.Stages, value.Order)
            : (null, int.MaxValue);
}
