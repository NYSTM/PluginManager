namespace PluginManager;

/// <summary>
/// プラグイン実行結果を表します。
/// </summary>
public sealed class PluginExecutionResult
{
    /// <summary>
    /// 実行されたプラグインの記述子を取得します。
    /// </summary>
    public PluginDescriptor Descriptor { get; }

    /// <summary>
    /// 実行結果の値を取得します。実行されなかった場合やエラーの場合は <see langword="null"/>。
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// 実行中に発生した例外を取得します。成功した場合は <see langword="null"/>。
    /// </summary>
    public Exception? Error { get; }

    /// <summary>
    /// エラー情報を取得します。エラーが発生していない場合は <see langword="null"/>。
    /// </summary>
    public PluginErrorInfo? ErrorInfo { get; }

    /// <summary>
    /// プラグインがスキップされたかどうかを取得します。
    /// </summary>
    public bool Skipped { get; }

    /// <summary>
    /// スキップされた理由を取得します。スキップされていない場合は <see langword="null"/>。
    /// </summary>
    public string? SkipReason { get; }

    /// <summary>
    /// 実行が成功したかどうかを取得します。
    /// </summary>
    /// <remarks>
    /// スキップされた場合も失敗ではないため <see langword="true"/> になります。
    /// </remarks>
    public bool Success => Error is null;

    /// <summary>
    /// 実行された結果を生成します。
    /// </summary>
    /// <param name="descriptor">プラグインの記述子。</param>
    /// <param name="value">実行結果の値。</param>
    /// <param name="error">実行中に発生した例外。成功の場合は <see langword="null"/>。</param>
    public PluginExecutionResult(PluginDescriptor descriptor, object? value, Exception? error)
    {
        Descriptor = descriptor;
        Value = value;
        Error = error;
        ErrorInfo = error is not null ? PluginErrorInfo.ExecutionFailure(error) : null;
        Skipped = false;
        SkipReason = null;
    }

    /// <summary>
    /// スキップされた結果を生成します。
    /// </summary>
    private PluginExecutionResult(PluginDescriptor descriptor, string skipReason)
    {
        Descriptor = descriptor;
        Value = null;
        Error = null;
        ErrorInfo = null;
        Skipped = true;
        SkipReason = skipReason;
    }

    /// <summary>
    /// スキップされた結果を生成します。
    /// </summary>
    /// <param name="descriptor">プラグインの記述子。</param>
    /// <param name="reason">スキップされた理由。</param>
    /// <returns>スキップされた <see cref="PluginExecutionResult"/>。</returns>
    public static PluginExecutionResult CreateSkipped(PluginDescriptor descriptor, string reason)
        => new(descriptor, reason);
}
