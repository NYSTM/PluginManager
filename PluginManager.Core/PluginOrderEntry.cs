namespace PluginManager;

/// <summary>
/// プラグインの実行順序を表す設定エントリです。
/// </summary>
public sealed class PluginOrderEntry
{
    /// <summary>対象プラグインのID。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>実行順序。値が小さいほど先に実行されます。</summary>
    public int Order { get; init; }
}
