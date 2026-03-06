namespace PluginManager;

/// <summary>
/// プラグインの識別情報と型情報を保持する記述子です。
/// </summary>
public sealed record PluginDescriptor(string Id,
                                      string Name,
                                      Version Version,
                                      Type PluginType,
                                      string AssemblyPath,
                                      IReadOnlySet<PluginStage> SupportedStages);
