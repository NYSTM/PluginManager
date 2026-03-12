namespace PluginManager;

internal interface IPluginRuntime
{
    PluginIsolationMode IsolationMode { get; }

    Task<PluginLoadResult> LoadAsync(
        PluginDescriptor descriptor,
        PluginContext context,
        CancellationToken cancellationToken);

    void Unload(string assemblyPath);

    Task UnloadAsync(string assemblyPath, CancellationToken cancellationToken = default);

    void UnloadAll();
}
