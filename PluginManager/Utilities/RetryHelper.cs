namespace PluginManager;

/// <summary>
/// リトライ可能な非同期操作を実行するための汎用ヘルパークラスです。
/// </summary>
/// <remarks>
/// このクラスは、タイムアウト・リトライ・恒久的エラー判定を組み合わせた
/// 堅牢な非同期操作パターンを提供します。
/// </remarks>
internal static class RetryHelper
{
    /// <summary>
    /// 指定された操作をリトライポリシーに従って実行します。
    /// </summary>
    /// <typeparam name="TResult">操作の結果型。</typeparam>
    /// <param name="operation">実行する非同期操作。</param>
    /// <param name="isSuccess">結果が成功かどうかを判定する関数。</param>
    /// <param name="isPermanentError">エラーが恒久的（リトライ不可）かどうかを判定する関数。</param>
    /// <param name="timeoutMilliseconds">操作のタイムアウト時間（ミリ秒）。0 以下で無制限。</param>
    /// <param name="retryCount">最大リトライ回数。0 でリトライなし。</param>
    /// <param name="retryDelayMilliseconds">リトライ間の待機時間（ミリ秒）。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <param name="onStart">操作開始時のコールバック（試行回数が渡される）。</param>
    /// <param name="onSuccess">操作成功時のコールバック（試行回数・結果が渡される）。</param>
    /// <param name="onRetry">リトライ発生時のコールバック（試行回数・エラーが渡される）。</param>
    /// <param name="onFailed">最終的に失敗した時のコールバック（試行回数・エラーが渡される）。</param>
    /// <returns>操作の最終結果。</returns>
    public static async Task<TResult> ExecuteWithRetryAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        Func<TResult, bool> isSuccess,
        Func<TResult, bool> isPermanentError,
        int timeoutMilliseconds,
        int retryCount,
        int retryDelayMilliseconds,
        CancellationToken cancellationToken,
        Action<int>? onStart = null,
        Action<int, TResult>? onSuccess = null,
        Action<int, TResult>? onRetry = null,
        Action<int, TResult>? onFailed = null)
    {
        TResult result = default!;

        for (int attempt = 0; attempt <= retryCount; attempt++)
        {
            if (attempt == 0)
                onStart?.Invoke(attempt + 1);

            result = await ExecuteWithTimeoutAsync(operation, timeoutMilliseconds, cancellationToken);

            if (isSuccess(result))
            {
                onSuccess?.Invoke(attempt + 1, result);
                return result;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                onFailed?.Invoke(attempt + 1, result);
                return result;
            }

            if (isPermanentError(result))
            {
                onFailed?.Invoke(attempt + 1, result);
                return result;
            }

            if (attempt < retryCount)
            {
                onRetry?.Invoke(attempt + 1, result);
                try { await Task.Delay(retryDelayMilliseconds, cancellationToken); }
                catch (OperationCanceledException) { }
                continue;
            }

            onFailed?.Invoke(attempt + 1, result);
        }

        return result;
    }

    /// <summary>
    /// 指定された操作をタイムアウト付きで実行します。
    /// </summary>
    /// <typeparam name="TResult">操作の結果型。</typeparam>
    /// <param name="operation">実行する非同期操作。</param>
    /// <param name="timeoutMilliseconds">タイムアウト時間（ミリ秒）。0 以下で無制限。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>操作の結果。</returns>
    /// <remarks>
    /// タイムアウトが発生した場合、<see cref="OperationCanceledException"/> がスローされます。
    /// 呼び出し元でキャッチして適切な結果型に変換してください。
    /// </remarks>
    private static async Task<TResult> ExecuteWithTimeoutAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        if (timeoutMilliseconds <= 0)
            return await operation(cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMilliseconds);

        return await operation(cts.Token);
    }
}
