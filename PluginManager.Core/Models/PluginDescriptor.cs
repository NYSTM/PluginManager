namespace PluginManager;

/// <summary>
/// プラグインの識別情報と型情報を保持する記述子です。
/// </summary>
public sealed record PluginDescriptor(string Id,
                                      string Name,
                                      Version Version,
                                      string PluginTypeName,
                                      string AssemblyPath,
                                      IReadOnlySet<PluginStage> SupportedStages)
{
    /// <summary>
    /// プラグインの隔離方式を取得します。
    /// </summary>
    public PluginIsolationMode IsolationMode { get; init; } = PluginIsolationMode.InProcess;
}
