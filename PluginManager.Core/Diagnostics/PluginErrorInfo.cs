namespace PluginManager;

/// <summary>
/// プラグインエラーの詳細情報を表します。
/// </summary>
/// <param name="Category">エラーカテゴリ。</param>
/// <param name="Exception">発生した例外。</param>
/// <param name="Message">エラーメッセージ。</param>
/// <param name="IsRetryable">リトライ可能かどうか。</param>
/// <remarks>
/// <para>
/// <see cref="PluginErrorInfo"/> は、エラーの分類とリトライ判定を提供します。
/// </para>
/// <para>
/// <b>使用例:</b>
/// <code>
/// var errorInfo = PluginErrorInfo.FromException(ex);
/// if (errorInfo.IsRetryable)
/// {
///     // リトライ処理
/// }
/// else
/// {
///     // エラーログ記録
/// }
/// </code>
/// </para>
/// </remarks>
public sealed record PluginErrorInfo(
    PluginErrorCategory Category,
    Exception Exception,
    string Message,
    bool IsRetryable)
{
    /// <summary>
    /// 例外からエラー情報を生成します。
    /// </summary>
    /// <param name="exception">発生した例外。</param>
    /// <returns>分類されたエラー情報。</returns>
    public static PluginErrorInfo FromException(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => new PluginErrorInfo(
                PluginErrorCategory.Cancellation,
                exception,
                "処理がキャンセルされました。",
                IsRetryable: false),

            TimeoutException => new PluginErrorInfo(
                PluginErrorCategory.Timeout,
                exception,
                "処理がタイムアウトしました。",
                IsRetryable: true),

            InvalidOperationException when exception.Message.Contains("IPlugin") => new PluginErrorInfo(
                PluginErrorCategory.ContractViolation,
                exception,
                "プラグインの契約違反が検出されました。",
                IsRetryable: false),

            InvalidOperationException when exception.Message.Contains("依存") => new PluginErrorInfo(
                PluginErrorCategory.DependencyFailure,
                exception,
                "依存関係の解決に失敗しました。",
                IsRetryable: false),

            FileNotFoundException or DirectoryNotFoundException => new PluginErrorInfo(
                PluginErrorCategory.PermanentFailure,
                exception,
                "必須ファイルまたはディレクトリが見つかりません。",
                IsRetryable: false),

            System.Net.Sockets.SocketException or System.Net.Http.HttpRequestException => new PluginErrorInfo(
                PluginErrorCategory.TransientFailure,
                exception,
                "ネットワークエラーが発生しました。",
                IsRetryable: true),

            System.IO.IOException when exception.HResult == unchecked((int)0x80070020) => new PluginErrorInfo(
                PluginErrorCategory.TransientFailure,
                exception,
                "ファイルが他のプロセスで使用中です。",
                IsRetryable: true),

            _ => new PluginErrorInfo(
                PluginErrorCategory.Unknown,
                exception,
                exception.Message,
                IsRetryable: false)
        };
    }

    /// <summary>
    /// 初期化失敗エラーを生成します。
    /// </summary>
    /// <param name="exception">発生した例外。</param>
    /// <returns>初期化失敗エラー情報。</returns>
    public static PluginErrorInfo InitializationFailure(Exception exception)
        => new(
            PluginErrorCategory.InitializationFailure,
            exception,
            $"プラグインの初期化に失敗しました: {exception.Message}",
            IsRetryable: IsTransient(exception));

    /// <summary>
    /// 実行失敗エラーを生成します。
    /// </summary>
    /// <param name="exception">発生した例外。</param>
    /// <returns>実行失敗エラー情報。</returns>
    public static PluginErrorInfo ExecutionFailure(Exception exception)
        => new(
            PluginErrorCategory.ExecutionFailure,
            exception,
            $"プラグインの実行に失敗しました: {exception.Message}",
            IsRetryable: IsTransient(exception));

    /// <summary>
    /// 例外が一時的なエラーかどうかを判定します。
    /// </summary>
    private static bool IsTransient(Exception exception)
    {
        return exception switch
        {
            TimeoutException => true,
            System.Net.Sockets.SocketException => true,
            System.Net.Http.HttpRequestException => true,
            System.IO.IOException ioEx when ioEx.HResult == unchecked((int)0x80070020) => true,
            _ => false
        };
    }
}
