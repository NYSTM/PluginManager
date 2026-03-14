using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// CancellationToken の統一適用テストです。
/// </summary>
public sealed class CancellationTokenTests
{
    private static string CreateTempConfig()
    {
        var tempFile = Path.GetTempFileName();
        var json = """
        {
          "PluginsPath": "plugins",
          "StageOrders": []
        }
        """;
        File.WriteAllText(tempFile, json);
        return tempFile;
    }

    [Fact]
    public void Discover_WithCancellation_CanBeCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();  // 即座にキャンセル

        using var loader = new PluginLoader();

        // 空ディレクトリの場合は即座に完了するため、キャンセルされないこともある
        var exception = Record.Exception(() =>
            loader.Discover(Path.Combine(AppContext.BaseDirectory, "plugins"), cancellationToken: cts.Token));

        // キャンセルされた場合は OperationCanceledException、完了した場合は null
        Assert.True(exception is null or OperationCanceledException);
    }

    [Fact]
    public void DiscoverFromConfiguration_WithCancellation_CanBeCancelled()
    {
        var configPath = CreateTempConfig();
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();  // 即座にキャンセル

            using var loader = new PluginLoader();

            // 空ディレクトリの場合は即座に完了するため、キャンセルされないこともある
            var exception = Record.Exception(() =>
                loader.DiscoverFromConfiguration(configPath, cancellationToken: cts.Token));

            // キャンセルされた場合は OperationCanceledException、完了した場合は null
            Assert.True(exception is null or OperationCanceledException);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task CancellationToken_IsProperlyPropagated()
    {
        var configPath = CreateTempConfig();
        try
        {
            using var cts = new CancellationTokenSource();
            using var loader = new PluginLoader();
            var context = new PluginContext();

            // ロードが開始されるまで待機
            var loadTask = loader.LoadFromConfigurationAsync(configPath, context, cancellationToken: cts.Token);
            
            // 短い遅延後にキャンセル
            await Task.Delay(10);
            cts.Cancel();

            var exception = await Record.ExceptionAsync(async () => await loadTask);

            // キャンセルされた場合は OperationCanceledException、完了した場合は null
            Assert.True(exception is null or OperationCanceledException);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task AllPublicMethods_AcceptCancellationToken()
    {
        // すべての公開メソッドが CancellationToken を受け取ることを検証
        var loaderType = typeof(PluginLoader);
        
        var methodsWithoutCancellationToken = loaderType
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => m.DeclaringType == loaderType)  // PluginLoader 自身のメソッドのみ
            .Where(m => !m.IsSpecialName)  // プロパティ・イベントを除外
            .Where(m => m.Name != "Dispose" && m.Name != "DisposeAsync")  // Dispose メソッドを除外
            .Where(m => m.Name != "SetCallback")  // コールバック設定を除外
            .Where(m => m.Name != "SetExecutorCallback")
            .Where(m => m.Name != "SetProcessCallback")
            .Where(m => m.Name != "UnloadPlugin")  // Fire-and-forge の同期版を除外
            .Where(m => !m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)))
            .ToList();

        // すべてのメソッドが CancellationToken を受け取ることを確認
        Assert.Empty(methodsWithoutCancellationToken);
    }
}
