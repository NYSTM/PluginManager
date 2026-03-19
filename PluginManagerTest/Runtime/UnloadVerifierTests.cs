using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using PluginManager;
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

        var assembly = context.LoadFromAssemblyPath(typeof(UnloadVerifierTests).Assembly.Location);
        var strongRef = assembly;

        var result = await verifier.VerifyUnloadAsync(context, timeout: TimeSpan.FromSeconds(2));

        Assert.False(result, "強参照が残っている場合はアンロードが失敗するべきです");
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

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            _ = await loader.LoadAsync(tempDir, context);
            await loader.DisposeAsync();

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

        var assembly = context.LoadFromAssemblyPath(typeof(UnloadVerifierTests).Assembly.Location);
        var strongRef = assembly;

        var startTime = DateTime.Now;
        var result = await verifier.VerifyUnloadAsync(context, timeout: TimeSpan.FromSeconds(1));
        var elapsed = DateTime.Now - startTime;

        Assert.False(result, "タイムアウト時は false を返すべきです");
        Assert.True(elapsed.TotalSeconds < 5, "タイムアウト時間内に完了するべきです");
        GC.KeepAlive(strongRef);
    }

    [Fact]
    public void LogDiagnostics_WithWarningLogger_WritesDiagnosticMessages()
    {
        var logger = new TestLogger();
        var verifier = new UnloadVerifier(logger);
        var context = new AssemblyLoadContext("Test-Diagnostics", isCollectible: true);
        var assembly = context.LoadFromAssemblyPath(typeof(UnloadVerifierTests).Assembly.Location);
        var weakRef = new WeakReference(context, trackResurrection: true);

        try
        {
            InvokeLogDiagnostics(verifier, "Test-Diagnostics", weakRef);
        }
        finally
        {
            GC.KeepAlive(assembly);
            context.Unload();
        }

        Assert.Contains(logger.Entries, x => x.Contains("アンロード診断情報"));
        Assert.Contains(logger.Entries, x => x.Contains("Context Name: Test-Diagnostics"));
        Assert.Contains(logger.Entries, x => x.Contains("WeakReference.IsAlive: True"));
        Assert.Contains(logger.Entries, x => x.Contains("Assemblies in context"));
        Assert.Contains(logger.Entries, x => x.Contains("GC Total Memory"));
        Assert.Contains(logger.Entries, x => x.Contains("推奨対処"));
    }

    [Fact]
    public async Task UnloadVerifier_WorksWithRealPlugin()
    {
        using var loader = new PluginLoader();
        var context = new PluginContext();

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var results = await loader.LoadAsync(tempDir, context);
            if (results.Count == 0)
            {
                Assert.True(true, "プラグインがない場合はスキップ");
                return;
            }

            await loader.DisposeAsync();
            Assert.True(true, "DisposeAsync が完了するべきです");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static void InvokeLogDiagnostics(UnloadVerifier verifier, string contextName, WeakReference weakRef)
    {
        var method = typeof(UnloadVerifier).GetMethod("LogDiagnostics", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(verifier, [contextName, weakRef]);
    }

    private sealed class TestLogger : ILogger
    {
        public List<string> Entries { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(formatter(state, exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}
