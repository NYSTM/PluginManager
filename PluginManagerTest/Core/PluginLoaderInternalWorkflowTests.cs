using System.Collections.Frozen;
using System.Reflection;
using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="PluginLoader"/> の内部ワークフローテストです。
/// </summary>
public sealed class PluginLoaderInternalWorkflowTests
{
    private const string RetryFilePathEnvironmentVariable = "PLUGINMANAGER_RETRY_FILE_PATH";

    [Fact]
    public async Task LoadPluginWithTimeoutAsync_WithValidPlugin_ReturnsSuccess()
    {
        using var loader = new PluginLoader();
        var descriptor = CreateDescriptor(typeof(WorkflowSuccessPlugin));

        var result = await InvokeLoadPluginWithTimeoutAsync(loader, descriptor, new PluginContext(), timeoutMilliseconds: 0, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Instance);
    }

    [Fact]
    public async Task LoadPluginWithTimeoutAsync_WhenPluginTimesOut_ReturnsTimeoutException()
    {
        using var loader = new PluginLoader();
        var descriptor = CreateDescriptor(typeof(WorkflowSlowPlugin));

        var result = await InvokeLoadPluginWithTimeoutAsync(loader, descriptor, new PluginContext(), timeoutMilliseconds: 50, CancellationToken.None);

        Assert.False(result.Success);
        Assert.IsType<TimeoutException>(result.Error);
    }

    [Fact]
    public async Task LoadPluginWithRetryAsync_WhenFirstAttemptFailsThenSecondSucceeds_RetriesAndSucceeds()
    {
        using var loader = new PluginLoader();
        var callback = new LoaderWorkflowCallback();
        loader.SetCallback(callback);
        var descriptor = CreateDescriptor(typeof(WorkflowRetryPlugin));
        var retryFile = Path.Combine(Path.GetTempPath(), $"plugin-loader-retry-{Guid.NewGuid():N}.txt");
        Environment.SetEnvironmentVariable(RetryFilePathEnvironmentVariable, retryFile);

        try
        {
            var result = await InvokeLoadPluginWithRetryAsync(
                loader,
                descriptor,
                new PluginContext(),
                timeoutMilliseconds: 0,
                retryCount: 1,
                retryDelayMilliseconds: 1,
                CancellationToken.None,
                "exec-retry");

            Assert.True(result.Success);
            Assert.Equal([1], callback.PluginLoadStartAttempts);
            Assert.Equal([1], callback.PluginLoadRetryAttempts);
            Assert.Equal([2], callback.PluginLoadSuccessAttempts);
            Assert.Empty(callback.PluginLoadFailedAttempts);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RetryFilePathEnvironmentVariable, null);
            if (File.Exists(retryFile))
                File.Delete(retryFile);
        }
    }

    [Fact]
    public async Task LoadPluginWithRetryAsync_WhenPermanentError_FailsWithoutRetry()
    {
        using var loader = new PluginLoader();
        var callback = new LoaderWorkflowCallback();
        loader.SetCallback(callback);
        var descriptor = CreateDescriptor(typeof(WorkflowPermanentFailurePlugin));

        var result = await InvokeLoadPluginWithRetryAsync(
            loader,
            descriptor,
            new PluginContext(),
            timeoutMilliseconds: 0,
            retryCount: 3,
            retryDelayMilliseconds: 1,
            CancellationToken.None,
            "exec-permanent");

        Assert.False(result.Success);
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Equal([1], callback.PluginLoadStartAttempts);
        Assert.Empty(callback.PluginLoadRetryAttempts);
        Assert.Empty(callback.PluginLoadSuccessAttempts);
        Assert.Equal([1], callback.PluginLoadFailedAttempts);
    }

    [Fact]
    public async Task LoadPluginWithIntervalAsync_WhenIntervalSpecified_WaitsBeforeReturning()
    {
        using var loader = new PluginLoader();
        var descriptor = CreateDescriptor(typeof(WorkflowSuccessPlugin));
        var start = DateTime.UtcNow;

        var result = await InvokeLoadPluginWithIntervalAsync(
            loader,
            descriptor,
            new PluginContext(),
            intervalMilliseconds: 100,
            timeoutMilliseconds: 0,
            retryCount: 0,
            retryDelayMilliseconds: 1,
            CancellationToken.None,
            "exec-interval");

        var elapsed = DateTime.UtcNow - start;
        Assert.True(result.Success);
        Assert.True(elapsed.TotalMilliseconds >= 80, $"経過時間が短すぎます: {elapsed.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task CompleteExecuteAsync_WhenTaskFails_PublishesFailedAndRethrows()
    {
        using var loader = new PluginLoader();
        var callback = new LoaderWorkflowCallback();
        loader.SetCallback(callback);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeCompleteExecuteAsync(
                loader,
                Task.FromException<IReadOnlyList<PluginExecutionResult>>(new InvalidOperationException("実行失敗")),
                PluginStage.Processing.Id,
                "exec-complete-failed"));

        Assert.Equal(PluginStage.Processing.Id, callback.ExecuteFailedStageId);
    }

    private static PluginDescriptor CreateDescriptor(Type pluginType)
        => new(
            pluginType.FullName!,
            pluginType.Name,
            new Version(1, 0, 0),
            pluginType.FullName!,
            pluginType.Assembly.Location,
            new[] { PluginStage.Processing }.ToFrozenSet());

    private static Task<PluginLoadResult> InvokeLoadPluginWithTimeoutAsync(
        PluginLoader loader,
        PluginDescriptor descriptor,
        PluginContext context,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        var method = typeof(PluginLoader).GetMethod("LoadPluginWithTimeoutAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task<PluginLoadResult>)method!.Invoke(loader, [descriptor, context, timeoutMilliseconds, cancellationToken])!;
    }

    private static Task<PluginLoadResult> InvokeLoadPluginWithRetryAsync(
        PluginLoader loader,
        PluginDescriptor descriptor,
        PluginContext context,
        int timeoutMilliseconds,
        int retryCount,
        int retryDelayMilliseconds,
        CancellationToken cancellationToken,
        string executionId)
    {
        var method = typeof(PluginLoader).GetMethod("LoadPluginWithRetryAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task<PluginLoadResult>)method!.Invoke(loader, [descriptor, context, timeoutMilliseconds, retryCount, retryDelayMilliseconds, cancellationToken, executionId])!;
    }

    private static Task<PluginLoadResult> InvokeLoadPluginWithIntervalAsync(
        PluginLoader loader,
        PluginDescriptor descriptor,
        PluginContext context,
        int intervalMilliseconds,
        int timeoutMilliseconds,
        int retryCount,
        int retryDelayMilliseconds,
        CancellationToken cancellationToken,
        string executionId)
    {
        var method = typeof(PluginLoader).GetMethod("LoadPluginWithIntervalAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task<PluginLoadResult>)method!.Invoke(loader, [descriptor, context, intervalMilliseconds, timeoutMilliseconds, retryCount, retryDelayMilliseconds, cancellationToken, executionId])!;
    }

    private static Task<IReadOnlyList<PluginExecutionResult>> InvokeCompleteExecuteAsync(
        PluginLoader loader,
        Task<IReadOnlyList<PluginExecutionResult>> executeTask,
        string stageId,
        string executionId)
    {
        var method = typeof(PluginLoader).GetMethod("CompleteExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task<IReadOnlyList<PluginExecutionResult>>)method!.Invoke(loader, [executeTask, stageId, executionId])!;
    }

    private sealed class LoaderWorkflowCallback : IPluginLoaderCallback
    {
        public List<int> PluginLoadStartAttempts { get; } = [];
        public List<int> PluginLoadRetryAttempts { get; } = [];
        public List<int> PluginLoadSuccessAttempts { get; } = [];
        public List<int> PluginLoadFailedAttempts { get; } = [];
        public string? ExecuteFailedStageId { get; private set; }

        public void OnPluginLoadStart(string pluginId, int attempt) => PluginLoadStartAttempts.Add(attempt);
        public void OnPluginLoadRetry(string pluginId, int attempt, Exception? error) => PluginLoadRetryAttempts.Add(attempt);
        public void OnPluginLoadSuccess(string pluginId, int attempt) => PluginLoadSuccessAttempts.Add(attempt);
        public void OnPluginLoadFailed(string pluginId, int attempt, Exception? error) => PluginLoadFailedAttempts.Add(attempt);
        public void OnExecuteFailed(string stageId, Exception error) => ExecuteFailedStageId = stageId;
    }

    private sealed class WorkflowSuccessPlugin : IPlugin
    {
        public string Id => nameof(WorkflowSuccessPlugin);
        public string Name => nameof(WorkflowSuccessPlugin);
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } = new[] { PluginStage.Processing }.ToFrozenSet();
        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default) => Task.FromResult<object?>("ok");
    }

    private sealed class WorkflowSlowPlugin : IPlugin
    {
        public string Id => nameof(WorkflowSlowPlugin);
        public string Name => nameof(WorkflowSlowPlugin);
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } = new[] { PluginStage.Processing }.ToFrozenSet();
        public async Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
            => await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default) => Task.FromResult<object?>("slow");
    }

    private sealed class WorkflowRetryPlugin : IPlugin
    {
        public string Id => nameof(WorkflowRetryPlugin);
        public string Name => nameof(WorkflowRetryPlugin);
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } = new[] { PluginStage.Processing }.ToFrozenSet();

        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
        {
            var path = Environment.GetEnvironmentVariable(RetryFilePathEnvironmentVariable);
            Assert.False(string.IsNullOrWhiteSpace(path));
            var current = File.Exists(path) ? int.Parse(File.ReadAllText(path)) : 0;
            current++;
            File.WriteAllText(path!, current.ToString());
            if (current == 1)
                throw new Exception("一時失敗");
            return Task.CompletedTask;
        }

        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default) => Task.FromResult<object?>("retry-ok");
    }

    private sealed class WorkflowPermanentFailurePlugin : IPlugin
    {
        public string Id => nameof(WorkflowPermanentFailurePlugin);
        public string Name => nameof(WorkflowPermanentFailurePlugin);
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } = new[] { PluginStage.Processing }.ToFrozenSet();
        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("恒久失敗");
        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default) => Task.FromResult<object?>(null);
    }
}
