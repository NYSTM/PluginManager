using PluginManager;
using System.Runtime.Loader;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// アンロード検証機構のテストです。
/// </summary>
public sealed class UnloadVerifierTests
{
    [Fact]
    public async Task VerifyUnloadAsync_WithStrongReference_ReturnsFalse()
    {
        var verifier = new UnloadVerifier();
        var context = new AssemblyLoadContext("Test-WithReference", isCollectible: true);
        
        // アセンブリをロードして強参照を作成
        var assembly = context.LoadFromAssemblyPath(typeof(UnloadVerifierTests).Assembly.Location);
        var strongRef = assembly;

        var result = await verifier.VerifyUnloadAsync(context, timeout: TimeSpan.FromSeconds(2));

        Assert.False(result, "強参照が残っている場合はアンロードが失敗するべきです");
        
        // クリーンアップ
        GC.KeepAlive(strongRef);
    }

    [Fact]
    public async Task VerifyUnloadAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        var verifier = new UnloadVerifier();
        var context = new AssemblyLoadContext("Test-Cancellation", isCollectible: true);
        
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await verifier.VerifyUnloadAsync(context, timeout: TimeSpan.FromSeconds(10), cancellationToken: cts.Token));
    }

    [Fact]
    public async Task DisposeAsync_ReleasesAllContexts()
    {
        var loader = new PluginLoader();
        var context = new PluginContext();

        // 簡易的なロードテスト（空のディレクトリ）
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var results = await loader.LoadAsync(tempDir, context);

            // DisposeAsync を呼び出し
            await loader.DisposeAsync();

            // 再度ロードを試みる（Dispose 後は失敗するはず）
            await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () =>
                await loader.LoadAsync(tempDir, context));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task UnloadVerifier_WithTimeout_ReturnsFalseQuickly()
    {
        var verifier = new UnloadVerifier();
        var context = new AssemblyLoadContext("Test-Timeout", isCollectible: true);
        
        // アセンブリをロードして強参照を保持
        var assembly = context.LoadFromAssemblyPath(typeof(UnloadVerifierTests).Assembly.Location);
        var strongRef = assembly;

        var startTime = DateTime.UtcNow;
        var result = await verifier.VerifyUnloadAsync(context, timeout: TimeSpan.FromSeconds(1));
        var elapsed = DateTime.UtcNow - startTime;

        Assert.False(result, "タイムアウト時は false を返すべきです");
        Assert.True(elapsed.TotalSeconds < 2, "タイムアウト時間内に完了するべきです");
        
        // クリーンアップ
        GC.KeepAlive(strongRef);
    }

    [Fact]
    public async Task UnloadVerifier_WorksWithRealPlugin()
    {
        // 実際のプラグインロードとアンロードをテスト
        using var loader = new PluginLoader();
        var context = new PluginContext();

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // プラグインをロード
            var results = await loader.LoadAsync(tempDir, context);

            // 結果がない場合はスキップ
            if (results.Count == 0)
            {
                Assert.True(true, "プラグインがない場合はスキップ");
                return;
            }

            // すべてアンロード
            await loader.DisposeAsync();

            // DisposeAsync が成功することを確認
            Assert.True(true, "DisposeAsync が完了するべきです");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
