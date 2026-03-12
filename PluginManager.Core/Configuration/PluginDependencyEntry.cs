namespace PluginManager;

/// <summary>
/// プラグインの依存関係を定義します。
/// </summary>
/// <remarks>
/// このエントリは、プラグインが他のプラグインに依存していることを明示的に宣言します。
/// トポロジカルソートにより、依存関係を考慮した実行順序が自動的に決定されます。
/// </remarks>
/// <example>
/// <code>
/// var dependency = new PluginDependencyEntry
/// {
///     Id = "database-plugin",
///     DependsOn = ["config-plugin", "logger-plugin"]
/// };
/// </code>
/// </example>
public sealed class PluginDependencyEntry
{
    /// <summary>
    /// プラグインの一意識別子を取得または設定します。
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// このプラグインが依存する他のプラグインの ID 一覧を取得または設定します。
    /// </summary>
    /// <remarks>
    /// 空のリストは依存関係がないことを意味します。
    /// 指定されたプラグインは、このプラグインより先に実行される必要があります。
    /// </remarks>
    public IReadOnlyList<string> DependsOn { get; init; } = [];
}
