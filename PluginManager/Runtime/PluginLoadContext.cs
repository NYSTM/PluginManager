using System.Reflection;
using System.Runtime.Loader;

namespace PluginManager;

/// <summary>
/// インプロセス実行プラグインをアセンブリ単位で分離するカスタムロードコンテキストです。
/// 別プロセス隔離は担当しません。
/// </summary>
internal sealed class PluginLoadContext(string pluginPath) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(pluginPath);

    /// <summary>
    /// ホスト側と型同一性を共有すべき契約アセンブリ名のセットです。
    /// これらは既定コンテキストから解決されるため、カスタム ALC へのロードを行いません。
    /// </summary>
    private static readonly System.Collections.Frozen.FrozenSet<string> SharedAssemblyNames =
        System.Collections.Frozen.FrozenSet.ToFrozenSet(
        [
            "PluginManager.Core",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
        ], StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 共有契約アセンブリは既定コンテキストに解決を委譲し、型同一性を保証する
        if (assemblyName.Name is not null && SharedAssemblyNames.Contains(assemblyName.Name))
            return null;

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
