using System.Reflection;
using System.Runtime.Loader;

namespace PluginManager;

/// <summary>
/// プラグインのアンロードを可能にするカスタムアセンブリロードコンテキスト。
/// 依存アセンブリとネイティブライブラリをプラグイン側で解決します。
/// </summary>
internal sealed class PluginLoadContext(string pluginPath) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(pluginPath);

    /// <inheritdoc/>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is not null ? LoadFromAssemblyPath(assemblyPath) : null;
    }

    /// <inheritdoc/>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is not null ? LoadUnmanagedDllFromPath(libraryPath) : IntPtr.Zero;
    }
}
