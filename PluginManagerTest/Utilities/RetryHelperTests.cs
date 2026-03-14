using System.Reflection;
using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="RetryHelper"/> のテストです。
/// </summary>
public sealed class RetryHelperTests
{
    [Fact]
    public async Task ExecuteWithRetryAsync_OnFirstSuccess_CallsStartAndSuccess()
    {
        var starts = new List<int>();
        var successes = new List<int>();
        var retries = new List<int>();
        var failures = new List<int>();

        var result = await RetryHelper.ExecuteWithRetryAsync(
            operation: _ => Task.FromResult(10),
            isSuccess: value => value == 10,
            isPermanentError: _ => false,
            timeoutMilliseconds: 0,
            retryCount: 2,
            retryDelayMilliseconds: 1,
            cancellationToken: CancellationToken.None,
            onStart: attempt => starts.Add(attempt),
            onSuccess: (attempt, _) => successes.Add(attempt),
            onRetry: (attempt, _) => retries.Add(attempt),
            onFailed: (attempt, _) => failures.Add(attempt));

        Assert.Equal(10, result);
        Assert.Equal([1], starts);
        Assert.Equal([1], successes);
        Assert.Empty(retries);
        Assert.Empty(failures);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WhenRetriableErrorThenSuccess_CallsRetryAndSuccess()
    {
        var attemptCount = 0;
        var retries = new List<int>();
        var successes = new List<int>();

        var result = await RetryHelper.ExecuteWithRetryAsync(
            operation: _ => Task.FromResult(++attemptCount),
            isSuccess: value => value >= 2,
            isPermanentError: _ => false,
            timeoutMilliseconds: 0,
            retryCount: 2,
            retryDelayMilliseconds: 1,
            cancellationToken: CancellationToken.None,
            onRetry: (attempt, _) => retries.Add(attempt),
            onSuccess: (attempt, _) => successes.Add(attempt));

        Assert.Equal(2, result);
        Assert.Equal([1], retries);
        Assert.Equal([2], successes);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WhenCancellationRequestedAfterFailure_CallsFailedAndReturns()
    {
        using var cts = new CancellationTokenSource();
        var failures = new List<int>();

        var result = await RetryHelper.ExecuteWithRetryAsync(
            operation: _ => Task.FromResult(1),
            isSuccess: _ => false,
            isPermanentError: _ => false,
            timeoutMilliseconds: 0,
            retryCount: 2,
            retryDelayMilliseconds: 1,
            cancellationToken: cts.Token,
            onFailed: (attempt, _) => failures.Add(attempt),
            onRetry: (_, _) => cts.Cancel());

        Assert.Equal(1, result);
        Assert.Equal([2], failures);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WhenPermanentError_CallsFailedWithoutRetry()
    {
        var retries = new List<int>();
        var failures = new List<int>();

        var result = await RetryHelper.ExecuteWithRetryAsync(
            operation: _ => Task.FromResult(5),
            isSuccess: _ => false,
            isPermanentError: _ => true,
            timeoutMilliseconds: 0,
            retryCount: 3,
            retryDelayMilliseconds: 1,
            cancellationToken: CancellationToken.None,
            onRetry: (attempt, _) => retries.Add(attempt),
            onFailed: (attempt, _) => failures.Add(attempt));

        Assert.Equal(5, result);
        Assert.Empty(retries);
        Assert.Equal([1], failures);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WhenRetryLimitReached_CallsFailed()
    {
        var retries = new List<int>();
        var failures = new List<int>();

        var result = await RetryHelper.ExecuteWithRetryAsync(
            operation: _ => Task.FromResult(7),
            isSuccess: _ => false,
            isPermanentError: _ => false,
            timeoutMilliseconds: 0,
            retryCount: 1,
            retryDelayMilliseconds: 1,
            cancellationToken: CancellationToken.None,
            onRetry: (attempt, _) => retries.Add(attempt),
            onFailed: (attempt, _) => failures.Add(attempt));

        Assert.Equal(7, result);
        Assert.Equal([1], retries);
        Assert.Equal([2], failures);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WhenDelayCancellationOccurs_SwallowsDelayCancellation()
    {
        using var cts = new CancellationTokenSource();
        var retries = new List<int>();
        var failures = new List<int>();

        var result = await RetryHelper.ExecuteWithRetryAsync(
            operation: _ => Task.FromResult(9),
            isSuccess: _ => false,
            isPermanentError: _ => false,
            timeoutMilliseconds: 0,
            retryCount: 1,
            retryDelayMilliseconds: 50,
            cancellationToken: cts.Token,
            onRetry: (_, _) =>
            {
                retries.Add(1);
                cts.Cancel();
            },
            onFailed: (attempt, _) => failures.Add(attempt));

        Assert.Equal(9, result);
        Assert.Equal([1], retries);
        Assert.Equal([2], failures);
    }

    [Fact]
    public async Task ExecuteWithTimeoutAsync_WhenTimeoutDisabled_UsesOriginalToken()
    {
        var tokenWasCancelable = false;
        var result = await InvokeExecuteWithTimeoutAsync(
            ct =>
            {
                tokenWasCancelable = ct.CanBeCanceled;
                return Task.FromResult(42);
            },
            timeoutMilliseconds: 0,
            cancellationToken: CancellationToken.None);

        Assert.Equal(42, result);
        Assert.False(tokenWasCancelable);
    }

    [Fact]
    public async Task ExecuteWithTimeoutAsync_WhenTimeoutEnabled_CancelsOperation()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            InvokeExecuteWithTimeoutAsync(
                async ct =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    return 0;
                },
                timeoutMilliseconds: 10,
                cancellationToken: CancellationToken.None));
    }

    private static Task<int> InvokeExecuteWithTimeoutAsync(
        Func<CancellationToken, Task<int>> operation,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        var method = typeof(RetryHelper).GetMethod("ExecuteWithTimeoutAsync", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var genericMethod = method!.MakeGenericMethod(typeof(int));
        return (Task<int>)genericMethod.Invoke(null, [operation, timeoutMilliseconds, cancellationToken])!;
    }
}
