namespace PluginManager;

/// <summary>
/// プラグイン実行結果を表します。
/// </summary>
public sealed record PluginExecutionResult(PluginDescriptor Descriptor, object? Value, Exception? Error)
{
    /// <summary>実行が成功したかどうかを取得します。</summary>
    public bool Success => Error is null;
}
