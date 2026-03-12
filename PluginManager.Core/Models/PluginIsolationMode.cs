namespace PluginManager;

/// <summary>
/// プラグインの隔離方式を表します。
/// </summary>
public enum PluginIsolationMode
{
    /// <summary>
    /// 同一プロセス内で <see cref="System.Runtime.Loader.AssemblyLoadContext"/> により依存関係を分離します。
    /// </summary>
    InProcess = 0,

    /// <summary>
    /// 別プロセスでプラグインを実行します。
    /// </summary>
    OutOfProcess = 1,
}
