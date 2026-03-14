using System.Runtime.Loader;

namespace PluginHost;

/// <summary>
/// プラグインアセンブリをロードするための分離されたコンテキストです。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AssemblyLoadContext"/> の collectible モードを使用し、
/// アンロード時にメモリからアセンブリを解放できるようにします。
/// </para>
/// <para>
/// <b>依存関係解決:</b><br/>
/// <see cref="System.Runtime.Loader.AssemblyDependencyResolver"/> を使用して、
/// プラグインアセンブリの依存関係を自動的に解決します。
/// </para>
/// </remarks>
internal class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    /// <summary>
    /// プラグインアセンブリパスを指定してコンテキストを初期化します。
    /// </summary>
    /// <param name="pluginPath">プラグインアセンブリの完全パス。</param>
    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new(pluginPath);
    }

    protected virtual string? ResolveAssemblyPath(System.Reflection.AssemblyName assemblyName)
        => _resolver.ResolveAssemblyToPath(assemblyName);

    protected virtual string? ResolveUnmanagedDllPath(string unmanagedDllName)
        => _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

    /// <summary>
    /// マネージドアセンブリを読み込みます。
    /// </summary>
    /// <param name="assemblyName">読み込むアセンブリ名。</param>
    /// <returns>読み込まれたアセンブリ。解決できない場合は <see langword="null"/>。</returns>
    protected override System.Reflection.Assembly? Load(System.Reflection.AssemblyName assemblyName)
    {
        var assemblyPath = ResolveAssemblyPath(assemblyName);
        return assemblyPath is not null ? LoadFromAssemblyPath(assemblyPath) : null;
    }

    /// <summary>
    /// アンマネージド DLL を読み込みます。
    /// </summary>
    /// <param name="unmanagedDllName">読み込む DLL 名。</param>
    /// <returns>読み込まれた DLL のハンドル。解決できない場合は <see cref="IntPtr.Zero"/>。</returns>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = ResolveUnmanagedDllPath(unmanagedDllName);
        return libraryPath is not null ? LoadUnmanagedDllFromPath(libraryPath) : IntPtr.Zero;
    }
}
