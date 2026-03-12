namespace PluginManager;

/// <summary>
/// プラグイン読み込み処理の結果を表します。
/// </summary>
public sealed record PluginLoadResult
{
    /// <summary>
    /// プラグインの記述子を取得します。
    /// </summary>
    public PluginDescriptor Descriptor { get; }

    /// <summary>
    /// ロードされたプラグインのインスタンスを取得します。ロード失敗時は <see langword="null"/>。
    /// </summary>
    public IPlugin? Instance { get; }

    /// <summary>
    /// ロード中に発生した例外を取得します。成功時は <see langword="null"/>。
    /// </summary>
    public Exception? Error { get; }

    /// <summary>
    /// エラー情報を取得します。エラーが発生していない場合は <see langword="null"/>。
    /// </summary>
    public PluginErrorInfo? ErrorInfo { get; }

    /// <summary>
    /// 読み込みが成功したかどうかを取得します。
    /// </summary>
    public bool Success => Instance is not null && Error is null;

    /// <summary>
    /// 成功したロード結果を生成します。
    /// </summary>
    /// <param name="descriptor">プラグインの記述子。</param>
    /// <param name="instance">ロードされたプラグインインスタンス。</param>
    public PluginLoadResult(PluginDescriptor descriptor, IPlugin instance)
    {
        Descriptor = descriptor;
        Instance = instance;
        Error = null;
        ErrorInfo = null;
    }

    /// <summary>
    /// 失敗したロード結果を生成します。
    /// </summary>
    /// <param name="descriptor">プラグインの記述子。</param>
    /// <param name="error">発生した例外。</param>
    public PluginLoadResult(PluginDescriptor descriptor, Exception error)
    {
        Descriptor = descriptor;
        Instance = null;
        Error = error;
        ErrorInfo = PluginErrorInfo.FromException(error);
    }

    /// <summary>
    /// 失敗したロード結果を生成します（エラー情報付き）。
    /// </summary>
    /// <param name="descriptor">プラグインの記述子。</param>
    /// <param name="errorInfo">エラー情報。</param>
    public PluginLoadResult(PluginDescriptor descriptor, PluginErrorInfo errorInfo)
    {
        Descriptor = descriptor;
        Instance = null;
        Error = errorInfo.Exception;
        ErrorInfo = errorInfo;
    }

    /// <summary>
    /// ロード結果を生成します（後方互換性のため）。
    /// </summary>
    /// <param name="descriptor">プラグインの記述子。</param>
    /// <param name="instance">ロードされたプラグインインスタンス。失敗時は <see langword="null"/>。</param>
    /// <param name="error">発生した例外。成功時は <see langword="null"/>。</param>
    public PluginLoadResult(PluginDescriptor descriptor, IPlugin? instance, Exception? error)
    {
        Descriptor = descriptor;
        Instance = instance;
        Error = error;
        ErrorInfo = error is not null ? PluginErrorInfo.FromException(error) : null;
    }
}
