using System.IO;
using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// エラー分類機能のテストです。
/// </summary>
public sealed class PluginErrorInfoTests
{
    [Fact]
    public void FromException_WithOperationCanceledException_ReturnsCancellationCategory()
    {
        var ex = new OperationCanceledException("キャンセルされました");

        var errorInfo = PluginErrorInfo.FromException(ex);

        Assert.Equal(PluginErrorCategory.Cancellation, errorInfo.Category);
        Assert.False(errorInfo.IsRetryable);
        Assert.Equal("処理がキャンセルされました。", errorInfo.Message);
    }

    [Fact]
    public void FromException_WithTimeoutException_ReturnsTimeoutCategory()
    {
        var ex = new TimeoutException("タイムアウトしました");

        var errorInfo = PluginErrorInfo.FromException(ex);

        Assert.Equal(PluginErrorCategory.Timeout, errorInfo.Category);
        Assert.True(errorInfo.IsRetryable);
        Assert.Equal("処理がタイムアウトしました。", errorInfo.Message);
    }

    [Fact]
    public void FromException_WithContractViolation_ReturnsContractViolationCategory()
    {
        var ex = new InvalidOperationException("型が IPlugin を実装していません");

        var errorInfo = PluginErrorInfo.FromException(ex);

        Assert.Equal(PluginErrorCategory.ContractViolation, errorInfo.Category);
        Assert.False(errorInfo.IsRetryable);
        Assert.Equal("プラグインの契約違反が検出されました。", errorInfo.Message);
    }

    [Fact]
    public void FromException_WithDependencyFailure_ReturnsDependencyFailureCategory()
    {
        var ex = new InvalidOperationException("依存プラグインが見つかりません");

        var errorInfo = PluginErrorInfo.FromException(ex);

        Assert.Equal(PluginErrorCategory.DependencyFailure, errorInfo.Category);
        Assert.False(errorInfo.IsRetryable);
        Assert.Equal("依存関係の解決に失敗しました。", errorInfo.Message);
    }

    [Fact]
    public void FromException_WithFileNotFoundException_ReturnsPermanentFailureCategory()
    {
        var ex = new FileNotFoundException("ファイルが見つかりません");

        var errorInfo = PluginErrorInfo.FromException(ex);

        Assert.Equal(PluginErrorCategory.PermanentFailure, errorInfo.Category);
        Assert.False(errorInfo.IsRetryable);
        Assert.Equal("必須ファイルまたはディレクトリが見つかりません。", errorInfo.Message);
    }

    [Fact]
    public void FromException_WithHttpRequestException_ReturnsTransientFailureCategory()
    {
        var ex = new System.Net.Http.HttpRequestException("ネットワークエラー");

        var errorInfo = PluginErrorInfo.FromException(ex);

        Assert.Equal(PluginErrorCategory.TransientFailure, errorInfo.Category);
        Assert.True(errorInfo.IsRetryable);
        Assert.Equal("ネットワークエラーが発生しました。", errorInfo.Message);
    }

    [Fact]
    public void FromException_WithSharingViolationIOException_ReturnsTransientFailureCategory()
    {
        var ex = new IOException("使用中")
        {
            HResult = unchecked((int)0x80070020),
        };

        var errorInfo = PluginErrorInfo.FromException(ex);

        Assert.Equal(PluginErrorCategory.TransientFailure, errorInfo.Category);
        Assert.True(errorInfo.IsRetryable);
        Assert.Equal("ファイルが他のプロセスで使用中です。", errorInfo.Message);
    }

    [Fact]
    public void FromException_WithUnknownException_ReturnsUnknownCategory()
    {
        var ex = new ArgumentException("不明なエラー");

        var errorInfo = PluginErrorInfo.FromException(ex);

        Assert.Equal(PluginErrorCategory.Unknown, errorInfo.Category);
        Assert.False(errorInfo.IsRetryable);
        Assert.Equal("不明なエラー", errorInfo.Message);
    }

    [Fact]
    public void InitializationFailure_CreatesCorrectErrorInfo()
    {
        var ex = new Exception("DB 接続失敗");

        var errorInfo = PluginErrorInfo.InitializationFailure(ex);

        Assert.Equal(PluginErrorCategory.InitializationFailure, errorInfo.Category);
        Assert.Contains("初期化に失敗しました", errorInfo.Message);
        Assert.Contains("DB 接続失敗", errorInfo.Message);
    }

    [Fact]
    public void InitializationFailure_WithTransientException_IsRetryable()
    {
        var ex = new TimeoutException("timeout");

        var errorInfo = PluginErrorInfo.InitializationFailure(ex);

        Assert.True(errorInfo.IsRetryable);
    }

    [Fact]
    public void ExecutionFailure_CreatesCorrectErrorInfo()
    {
        var ex = new Exception("処理エラー");

        var errorInfo = PluginErrorInfo.ExecutionFailure(ex);

        Assert.Equal(PluginErrorCategory.ExecutionFailure, errorInfo.Category);
        Assert.Contains("実行に失敗しました", errorInfo.Message);
        Assert.Contains("処理エラー", errorInfo.Message);
    }

    [Fact]
    public void ExecutionFailure_WithSharingViolationIOException_IsRetryable()
    {
        var ex = new IOException("使用中")
        {
            HResult = unchecked((int)0x80070020),
        };

        var errorInfo = PluginErrorInfo.ExecutionFailure(ex);

        Assert.True(errorInfo.IsRetryable);
    }

    [Fact]
    public void PluginLoadResult_WithException_SetsErrorInfoAutomatically()
    {
        var descriptor = CreateTestDescriptor();
        var ex = new TimeoutException("タイムアウト");

        var result = new PluginLoadResult(descriptor, ex);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorInfo);
        Assert.Equal(PluginErrorCategory.Timeout, result.ErrorInfo!.Category);
        Assert.True(result.ErrorInfo.IsRetryable);
    }

    [Fact]
    public void PluginLoadResult_WithErrorInfo_SetsErrorAndErrorInfo()
    {
        var descriptor = CreateTestDescriptor();
        var errorInfo = PluginErrorInfo.ExecutionFailure(new InvalidOperationException("error"));

        var result = new PluginLoadResult(descriptor, errorInfo);

        Assert.False(result.Success);
        Assert.Same(errorInfo, result.ErrorInfo);
        Assert.Same(errorInfo.Exception, result.Error);
    }

    [Fact]
    public void PluginExecutionResult_WithException_SetsErrorInfoAutomatically()
    {
        var descriptor = CreateTestDescriptor();
        var ex = new InvalidOperationException("実行エラー");

        var result = new PluginExecutionResult(descriptor, null, ex);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorInfo);
        Assert.Equal(PluginErrorCategory.ExecutionFailure, result.ErrorInfo!.Category);
    }

    [Fact]
    public void IsRetryable_CorrectlyIdentifiesTransientErrors()
    {
        var transientErrors = new Exception[]
        {
            new TimeoutException(),
            new System.Net.Sockets.SocketException(),
            new System.Net.Http.HttpRequestException()
        };

        foreach (var ex in transientErrors)
        {
            var errorInfo = PluginErrorInfo.FromException(ex);
            Assert.True(errorInfo.IsRetryable, $"{ex.GetType().Name} should be retryable");
        }
    }

    [Fact]
    public void IsRetryable_CorrectlyIdentifiesPermanentErrors()
    {
        var permanentErrors = new Exception[]
        {
            new OperationCanceledException(),
            new InvalidOperationException("IPlugin 未実装"),
            new FileNotFoundException(),
            new DirectoryNotFoundException()
        };

        foreach (var ex in permanentErrors)
        {
            var errorInfo = PluginErrorInfo.FromException(ex);
            Assert.False(errorInfo.IsRetryable, $"{ex.GetType().Name} should not be retryable");
        }
    }

    private static PluginDescriptor CreateTestDescriptor()
        => new(
            "test-plugin",
            "Test Plugin",
            new Version(1, 0, 0),
            typeof(object).FullName!,
            "test.dll",
            new HashSet<PluginStage> { PluginStage.Processing })
        {
            IsolationMode = PluginIsolationMode.InProcess
        };
}
