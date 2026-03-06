namespace PluginManager;

/// <summary>
/// プラグイン読み込み処理の結果を表します。
/// </summary>
public sealed record PluginLoadResult(PluginDescriptor Descriptor,
                                      IPlugin? Instance,
                                      Exception? Error)
{
    /// <summary>
    /// 読み込みが成功したかどうかを取得します。
    /// </summary>
    public bool Success => Instance is not null && Error is null;
}
