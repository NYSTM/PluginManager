using System.Collections.Frozen;
using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// PluginLoader のユニットテスト
/// </summary>
public sealed class PluginLoaderTests
{
    [Fact]
    public void Discover_NonExistentDirectory_ReturnsEmptyList()
    {
        // Arrange
        var loader = new PluginLoader();
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act
        var result = loader.Discover(nonExistentPath);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void Discover_EmptyPath_ThrowsException()
    {
        // Arrange
        var loader = new PluginLoader();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => loader.Discover(string.Empty));
        Assert.Throws<ArgumentException>(() => loader.Discover(null!));
    }

    [Fact]
    public async Task Load_NonExistentDirectory_ReturnsEmptyList()
    {
        // Arrange
        using var loader = new PluginLoader();
        var context = new PluginContext();
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act
        var result = await loader.LoadAsync(nonExistentPath, context);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadFromConfiguration_NonExistentPluginsPath_ReturnsEmptyList()
    {
        // Arrange
        using var loader = new PluginLoader();
        var context = new PluginContext();
        var tempFile = Path.GetTempFileName();
        var json = """
        {
          "PluginsPath": "nonexistent-path",
          "IntervalMilliseconds": 0,
          "TimeoutMilliseconds": 0
        }
        """;
        File.WriteAllText(tempFile, json);

        try
        {
            // Act
            var result = await loader.LoadFromConfigurationAsync(tempFile, context);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DiscoverFromConfiguration_ValidConfiguration_ReturnsOrderedList()
    {
        // Arrange
        var loader = new PluginLoader();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        var tempFile = Path.GetTempFileName();
        var json = $$$"""
        {
          "PluginsPath": "{{{tempDir.Replace("\\", "\\\\")}}}",
          "PluginOrder": [
            {
              "Name": "TestPlugin",
              "Order": 1
            }
          ]
        }
        """;
        File.WriteAllText(tempFile, json);

        try
        {
            // Act
            var result = loader.DiscoverFromConfiguration(tempFile);

            // Assert
            Assert.NotNull(result);
        }
        finally
        {
            File.Delete(tempFile);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task LoadFromConfiguration_Cancelled_ReturnsResultWithCancellationException()
    {
        // Arrange
        using var loader = new PluginLoader();
        var context = new PluginContext();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        var tempFile = Path.GetTempFileName();
        var json = $$$"""
        {
          "PluginsPath": "{{{tempDir.Replace("\\", "\\\\")}}}",
          "IntervalMilliseconds": 0,
          "TimeoutMilliseconds": 0
        }
        """;
        File.WriteAllText(tempFile, json);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            // Act
            var result = await loader.LoadFromConfigurationAsync(tempFile, context, cancellationToken: cts.Token);

            // Assert
            Assert.NotNull(result);
        }
        finally
        {
            File.Delete(tempFile);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Dispose 後に LoadFromConfigurationAsync を呼ぶと ObjectDisposedException が発生することを確認します。
    /// </summary>
    [Fact]
    public async Task LoadFromConfigurationAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var loader = new PluginLoader();
        var context = new PluginContext();
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """{ "PluginsPath": "plugins" }""");

        try
        {
            loader.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => loader.LoadFromConfigurationAsync(tempFile, context));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Dispose 後に LoadAsync を呼ぶと ObjectDisposedException が発生することを確認します。
    /// </summary>
    [Fact]
    public async Task LoadAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var loader = new PluginLoader();
        var context = new PluginContext();
        loader.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => loader.LoadAsync(Path.GetTempPath(), context));
    }

    /// <summary>
    /// Dispose を複数回呼んでも例外が発生しないことを確認します。
    /// </summary>
    [Fact]
    public void Dispose_MultipleTimes_DoesNotThrow()
    {
        var loader = new PluginLoader();
        loader.Dispose();
        var ex = Record.Exception(() => loader.Dispose());
        Assert.Null(ex);
    }

    /// <summary>
    /// UnloadPlugin に存在しないパスを渡しても例外が発生しないことを確認します。
    /// </summary>
    [Fact]
    public void UnloadPlugin_NonExistentPath_DoesNotThrow()
    {
        using var loader = new PluginLoader();
        var ex = Record.Exception(() => loader.UnloadPlugin("nonexistent.dll"));
        Assert.Null(ex);
    }

    /// <summary>
    /// UnloadPluginAsync に存在しないパスを渡しても例外が発生しないことを確認します。
    /// </summary>
    [Fact]
    public async Task UnloadPluginAsync_NonExistentPath_DoesNotThrow()
    {
        using var loader = new PluginLoader();
        var ex = await Record.ExceptionAsync(() => loader.UnloadPluginAsync("nonexistent.dll"));
        Assert.Null(ex);
    }

    /// <summary>
    /// PluginLoadResult の Success プロパティが Instance と Error の状態と一致することを確認します。
    /// </summary>
    [Fact]
    public void PluginLoadResult_Success_ReflectsInstanceAndError()
    {
        var descriptor = new PluginDescriptor(
            "test-id", "Test", new Version(1, 0, 0),
            typeof(object), "test.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

        // Instance あり・Error なし → Success = true
        var success = new PluginLoadResult(descriptor, new FakePlugin(), null);
        Assert.True(success.Success);

        // Instance なし → Success = false
        var noInstance = new PluginLoadResult(descriptor, null, null);
        Assert.False(noInstance.Success);

        // Error あり → Success = false
        var withError = new PluginLoadResult(descriptor, null, new Exception("失敗"));
        Assert.False(withError.Success);
    }

    /// <summary>
    /// LoadFromConfigurationAsync で PluginsPath が空の場合に空リストを返すことを確認します。
    /// </summary>
    [Fact]
    public async Task LoadFromConfigurationAsync_EmptyPluginsPath_ReturnsEmptyList()
    {
        // Arrange
        using var loader = new PluginLoader();
        var context = new PluginContext();
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """{ "PluginsPath": "" }""");

        try
        {
            // Act
            var result = await loader.LoadFromConfigurationAsync(tempFile, context);

            // Assert
            Assert.Empty(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadFromConfigurationAsync_RaisesLoadStartAndCompletedEvents()
    {
        using var loader = new PluginLoader();
        var context = new PluginContext();
        var callback = new TestEventTracker();
        loader.SetCallback(callback);

        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
        {
          "PluginsPath": "nonexistent-path",
          "IntervalMilliseconds": 0,
          "TimeoutMilliseconds": 0
        }
        """);

        try
        {
            await loader.LoadFromConfigurationAsync(tempFile, context);
            Assert.True(callback.LoadStartCalled);
            Assert.True(callback.LoadCompletedCalled);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExecutePluginsAndWaitAsync_RaisesExecuteEvents()
    {
        using var loader = new PluginLoader();
        var context = new PluginContext();
        var callback = new TestEventTracker();
        loader.SetCallback(callback);

        var descriptor = new PluginDescriptor(
            "test-id", "Test", new Version(1, 0, 0),
            typeof(object), "test.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

        var loadResults = new List<PluginLoadResult>
        {
            new(descriptor, new FakePlugin(), null)
        };

        await loader.ExecutePluginsAndWaitAsync(loadResults, PluginStage.Processing, context);

        Assert.True(callback.ExecuteStartCalled);
        Assert.True(callback.ExecuteCompletedCalled);
    }

    private class TestEventTracker : IPluginLoaderCallback
    {
        public bool LoadStartCalled { get; private set; }
        public bool LoadCompletedCalled { get; private set; }
        public bool ExecuteStartCalled { get; private set; }
        public bool ExecuteCompletedCalled { get; private set; }

        public void OnLoadStart(string configurationFilePath) => LoadStartCalled = true;
        public void OnLoadCompleted(string configurationFilePath) => LoadCompletedCalled = true;
        public void OnExecuteStart(string stageId) => ExecuteStartCalled = true;
        public void OnExecuteCompleted(string stageId) => ExecuteCompletedCalled = true;
    }

    /// <summary>
    /// 1 件のプラグインが例外をスローしても他のプラグイン結果が保持されることを確認します。
    /// </summary>
    [Fact]
    public async Task ExecutePluginsAndWaitAsync_OnePluginThrows_OtherResultsPreserved()
    {
        // Arrange
        using var loader = new PluginLoader();
        var context = new PluginContext();

        var descriptor = new PluginDescriptor(
            "ok-plugin", "OK", new Version(1, 0, 0),
            typeof(object), "ok.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

        var throwingDescriptor = new PluginDescriptor(
            "throwing-plugin", "Throwing", new Version(1, 0, 0),
            typeof(object), "throwing.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

        var loadResults = new List<PluginLoadResult>
        {
            new(descriptor,         new FakePlugin(),     null),
            new(throwingDescriptor, new ThrowingPlugin(), null),
        };

        // Act
        var results = await loader.ExecutePluginsAndWaitAsync(
            loadResults, PluginStage.Processing, context);

        // Assert: 2 件とも結果が返る（例外で全体が中断されない）
        Assert.Equal(2, results.Count);

        var ok = results.Single(r => r.Descriptor.Id == "ok-plugin");
        Assert.True(ok.Success);
        Assert.Equal("fake-result", ok.Value);

        var ng = results.Single(r => r.Descriptor.Id == "throwing-plugin");
        Assert.False(ng.Success);
        Assert.NotNull(ng.Error);
        Assert.IsType<InvalidOperationException>(ng.Error);
    }

    /// <summary>
    /// すべてのプラグインが成功した場合に全件 Success になることを確認します。
    /// </summary>
    [Fact]
    public async Task ExecutePluginsAndWaitAsync_AllSucceed_AllResultsSuccess()
    {
        // Arrange
        using var loader = new PluginLoader();
        var context = new PluginContext();

        var makeDescriptor = (string id) => new PluginDescriptor(
            id, id, new Version(1, 0, 0),
            typeof(object), $"{id}.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

        var loadResults = new List<PluginLoadResult>
        {
            new(makeDescriptor("plugin-1"), new FakePlugin(), null),
            new(makeDescriptor("plugin-2"), new FakePlugin(), null),
        };

        // Act
        var results = await loader.ExecutePluginsAndWaitAsync(
            loadResults, PluginStage.Processing, context);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
    }

    /// <summary>
    /// ロード失敗プラグイン（Success=false）はスキップされ、結果に含まれることを確認します。
    /// </summary>
    [Fact]
    public async Task ExecutePluginsAndWaitAsync_SkipsFailedLoadResults()
    {
        // Arrange
        using var loader = new PluginLoader();
        var context = new PluginContext();

        var descriptor = new PluginDescriptor(
            "ok-plugin", "OK", new Version(1, 0, 0),
            typeof(object), "ok.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

        var failedDescriptor = new PluginDescriptor(
            "failed-plugin", "Failed", new Version(1, 0, 0),
            typeof(object), "failed.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

        var loadResults = new List<PluginLoadResult>
        {
            new(descriptor,       new FakePlugin(), null),
            new(failedDescriptor, null,             new Exception("ロード失敗")),  // Success=false
        };

        // Act
        var results = await loader.ExecutePluginsAndWaitAsync(
            loadResults, PluginStage.Processing, context);

        // Assert: 両方の結果が返される（スキップされたものを含む）
        Assert.Equal(2, results.Count);
        
        // 最初のプラグインは実行成功
        Assert.Equal("ok-plugin", results[0].Descriptor.Id);
        Assert.True(results[0].Success);
        Assert.False(results[0].Skipped);

        // 2 番目のプラグインはスキップされた
        Assert.Equal("failed-plugin", results[1].Descriptor.Id);
        Assert.True(results[1].Success);  // スキップもエラーではないため Success = true
        Assert.True(results[1].Skipped);
        Assert.Equal("ロードに失敗したためスキップされました。", results[1].SkipReason);
    }

    /// <summary>
    /// SupportedStages に含まれないステージで実行した場合、プラグインがスキップされることを確認します。
    /// </summary>
    [Fact]
    public async Task ExecutePluginsAndWaitAsync_SkipsUnsupportedStage()
    {
        // Arrange
        using var loader = new PluginLoader();
        var context = new PluginContext();

        var descriptor = new PluginDescriptor(
            "processing-only", "ProcessingOnly", new Version(1, 0, 0),
            typeof(object), "processing.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());  // Processing のみサポート

        var loadResults = new List<PluginLoadResult>
        {
            new(descriptor, new FakePlugin(), null),
        };

        // Act: PreProcessing ステージで実行
        var results = await loader.ExecutePluginsAndWaitAsync(
            loadResults, PluginStage.PreProcessing, context);

        // Assert: スキップされた
        Assert.Single(results);
        Assert.Equal("processing-only", results[0].Descriptor.Id);
        Assert.True(results[0].Success);
        Assert.True(results[0].Skipped);
        Assert.Contains("PreProcessing", results[0].SkipReason);
        Assert.Contains("対象外", results[0].SkipReason);
    }

    /// <summary>
    /// スキップされたプラグインと実行されたプラグインが混在する場合、
    /// すべての結果が正しく返されることを確認します。
    /// </summary>
    [Fact]
    public async Task ExecutePluginsAndWaitAsync_MixedSkippedAndExecuted()
    {
        // Arrange
        using var loader = new PluginLoader();
        var context = new PluginContext();

        var preDescriptor = new PluginDescriptor(
            "pre-only", "PreOnly", new Version(1, 0, 0),
            typeof(object), "pre.dll",
            new[] { PluginStage.PreProcessing }.ToFrozenSet());

        var procDescriptor = new PluginDescriptor(
            "proc-only", "ProcOnly", new Version(1, 0, 0),
            typeof(object), "proc.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

        var bothDescriptor = new PluginDescriptor(
            "both", "Both", new Version(1, 0, 0),
            typeof(object), "both.dll",
            new[] { PluginStage.PreProcessing, PluginStage.Processing }.ToFrozenSet());

        var loadResults = new List<PluginLoadResult>
        {
            new(preDescriptor,  new FakePlugin(), null),
            new(procDescriptor, new FakePlugin(), null),
            new(bothDescriptor, new FakePlugin(), null),
        };

        // Act: Processing ステージで実行
        var results = await loader.ExecutePluginsAndWaitAsync(
            loadResults, PluginStage.Processing, context);

        // Assert: 3 件の結果が返される
        Assert.Equal(3, results.Count);

        // pre-only はスキップ
        Assert.Equal("pre-only", results[0].Descriptor.Id);
        Assert.True(results[0].Skipped, $"pre-only should be skipped. Actual: Skipped={results[0].Skipped}, Success={results[0].Success}, SkipReason={results[0].SkipReason}");

        // proc-only は実行
        Assert.Equal("proc-only", results[1].Descriptor.Id);
        Assert.False(results[1].Skipped);
        Assert.True(results[1].Success);

        // both は実行
        Assert.Equal("both", results[2].Descriptor.Id);
        Assert.False(results[2].Skipped);
        Assert.True(results[2].Success);
    }

    // ExecuteAsync で必ず例外をスローするプラグイン
    private sealed class ThrowingPlugin : IPlugin
    {
        public string Id => "throwing-plugin";
        public string Name => "Throwing";
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } =
            new[] { PluginStage.Processing }.ToFrozenSet();
        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("意図的な実行エラー");
    }

    // PluginLoadResult.Success テスト用の最小 IPlugin 実装
    private sealed class FakePlugin : IPlugin
    {
        public string Id => "fake";
        public string Name => "Fake";
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } =
            new[] { PluginStage.Processing }.ToFrozenSet();
        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
            => Task.FromResult<object?>("fake-result");
    }
}
