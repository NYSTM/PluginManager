using System.Reflection;
using System.Runtime.Loader;
using System.Collections.Frozen;
namespace PluginManager;

/// <summary>
/// インプロセス実行プラグインをアセンブリ単位で分離するカスタムロードコンテキストです。
/// 別プロセス隔離は担当しません。
/// </summary>
internal class PluginLoadContext(string pluginPath) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(pluginPath);

    /// <summary>
    /// ホスト側と型同一性を共有すべき契約アセンブリ名のセットです。
    /// これらは既定コンテキストから解決されるため、カスタム ALC へのロードを行いません。
    /// </summary>
    private static readonly FrozenSet<string> SharedAssemblyNames = FrozenSet.ToFrozenSet(
        [
            "PluginManager.Core",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
        ], StringComparer.OrdinalIgnoreCase);

    protected virtual string? ResolveAssemblyPath(AssemblyName assemblyName)
        => _resolver.ResolveAssemblyToPath(assemblyName);

    protected virtual string? ResolveUnmanagedDllPath(string unmanagedDllName)
        => _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

    /// <inheritdoc/>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 共有契約アセンブリは既定コンテキストに解決を委譲し、型同一性を保証する
        if (assemblyName.Name is not null && SharedAssemblyNames.Contains(assemblyName.Name))
            return null;

        var assemblyPath = ResolveAssemblyPath(assemblyName);
        return assemblyPath is not null ? LoadFromAssemblyPath(assemblyPath) : null;
    }

    /// <inheritdoc/>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = ResolveUnmanagedDllPath(unmanagedDllName);
        return libraryPath is not null ? LoadUnmanagedDllFromPath(libraryPath) : IntPtr.Zero;
    }
}
