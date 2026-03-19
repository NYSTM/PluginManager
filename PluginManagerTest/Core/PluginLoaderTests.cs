using System.Collections.Frozen;
using System.Reflection;
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
        var loader = new PluginLoader();
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = loader.Discover(nonExistentPath);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void Discover_EmptyPath_ThrowsException()
    {
        var loader = new PluginLoader();

        Assert.Throws<ArgumentException>(() => loader.Discover(string.Empty));
        Assert.Throws<ArgumentException>(() => loader.Discover(null!));
    }

    [Fact]
    public void SetExecutorCallback_WithNull_DoesNotThrow()
    {
        using var loader = new PluginLoader();
        var ex = Record.Exception(() => loader.SetExecutorCallback(null));
        Assert.Null(ex);
    }

    [Fact]
    public void SetProcessCallback_WithNull_DoesNotThrow()
    {
        using var loader = new PluginLoader();
        var ex = Record.Exception(() => loader.SetProcessCallback(null));
        Assert.Null(ex);
    }

    [Fact]
    public async Task Load_NonExistentDirectory_ReturnsEmptyList()
    {
        using var loader = new PluginLoader();
        var context = new PluginContext();
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = await loader.LoadAsync(nonExistentPath, context);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadFromConfiguration_NonExistentPluginsPath_ReturnsEmptyList()
    {
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
            var result = await loader.LoadFromConfigurationAsync(tempFile, context);

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
        var loader = new PluginLoader();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
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
            var result = loader.DiscoverFromConfiguration(tempFile);

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
    public async Task LoadFromConfiguration_Cancelled_ThrowsOperationCanceledException()
    {
        using var loader = new PluginLoader();
        var context = new PluginContext();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
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
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => loader.LoadFromConfigurationAsync(tempFile, context, cancellationToken: cts.Token));
        }
        finally
        {
            File.Delete(tempFile);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task LoadFromConfigurationAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var loader = new PluginLoader();
        var context = new PluginContext();
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """{ "PluginsPath": "plugins" }""");

        try
        {
            loader.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => loader.LoadFromConfigurationAsync(tempFile, context));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var loader = new PluginLoader();
        var context = new PluginContext();
        loader.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => loader.LoadAsync(Path.GetTempPath(), context));
    }

    [Fact]
    public void Dispose_MultipleTimes_DoesNotThrow()
    {
        var loader = new PluginLoader();
        loader.Dispose();
        var ex = Record.Exception(() => loader.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void UnloadPlugin_NonExistentPath_DoesNotThrow()
    {
        using var loader = new PluginLoader();
        var ex = Record.Exception(() => loader.UnloadPlugin("nonexistent.dll"));
        Assert.Null(ex);
    }

    [Fact]
    public async Task UnloadPluginAsync_NonExistentPath_DoesNotThrow()
    {
        using var loader = new PluginLoader();
        var ex = await Record.ExceptionAsync(() => loader.UnloadPluginAsync("nonexistent.dll"));
        Assert.Null(ex);
    }

    [Fact]
    public void PluginLoadResult_Success_ReflectsInstanceAndError()
    {
        var descriptor = new PluginDescriptor(
            "test-id", "Test", new Version(1, 0, 0),
            typeof(object).FullName!, "test.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

        var success = new PluginLoadResult(descriptor, new FakePlugin(), null);
        Assert.True(success.Success);

        var noInstance = new PluginLoadResult(descriptor, null, null);
        Assert.False(noInstance.Success);

        var withError = new PluginLoadResult(descriptor, null, new Exception("失敗"));
        Assert.False(withError.Success);
    }

    [Fact]
    public async Task LoadFromConfigurationAsync_EmptyPluginsPath_ReturnsEmptyList()
    {
        using var loader = new PluginLoader();
        var context = new PluginContext();
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """{ "PluginsPath": "" }""");

        try
        {
            var result = await loader.LoadFromConfigurationAsync(tempFile, context);
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
            typeof(object).FullName!, "test.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

        var loadResults = new List<PluginLoadResult>
        {
            new(descriptor, new FakePlugin(), null)
        };

        await loader.ExecutePluginsAndWaitAsync(loadResults, PluginStage.Processing, context);

        Assert.True(callback.ExecuteStartCalled);
        Assert.True(callback.ExecuteCompletedCalled);
    }

    [Fact]
    public async Task ExecutePluginsAndWaitAsync_OnePluginThrows_OtherResultsPreserved()
    {
        using var loader = new PluginLoader();
        var context = new PluginContext();

        var descriptor = new PluginDescriptor(
            "ok-plugin", "OK", new Version(1, 0, 0),
            typeof(object).FullName!, "ok.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

        var throwingDescriptor = new PluginDescriptor(
            "throwing-plugin", "Throwing", new Version(1, 0, 0),
            typeof(object).FullName!, "throwing.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

        var loadResults = new List<PluginLoadResult>
        {
            new(descriptor, new FakePlugin(), null),
            new(throwingDescriptor, new ThrowingPlugin(), null),
        };

        var results = await loader.ExecutePluginsAndWaitAsync(
            loadResults, PluginStage.Processing, context);

        Assert.Equal(2, results.Count);

        var ok = results.Single(r => r.Descriptor.Id == "ok-plugin");
        Assert.True(ok.Success);
        Assert.Equal("fake-result", ok.Value);

        var ng = results.Single(r => r.Descriptor.Id == "throwing-plugin");
        Assert.False(ng.Success);
        Assert.NotNull(ng.Error);
        Assert.IsType<InvalidOperationException>(ng.Error);
    }

    [Fact]
    public async Task ExecutePluginsAndWaitAsync_AllSucceed_AllResultsSuccess()
    {
        using var loader = new PluginLoader();
        var context = new PluginContext();

        var makeDescriptor = (string id) => new PluginDescriptor(
            id, id, new Version(1, 0, 0),
            typeof(object).FullName!, $"{id}.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

        var loadResults = new List<PluginLoadResult>
        {
            new(makeDescriptor("plugin-1"), new FakePlugin(), null),
            new(makeDescriptor("plugin-2"), new FakePlugin(), null),
        };

        var results = await loader.ExecutePluginsAndWaitAsync(
            loadResults, PluginStage.Processing, context);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task ExecutePluginsAndWaitAsync_SkipsFailedLoadResults()
    {
        using var loader = new PluginLoader();
        var context = new PluginContext();

        var descriptor = new PluginDescriptor(
            "ok-plugin", "OK", new Version(1, 0, 0),
            typeof(object).FullName!, "ok.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

        var failedDescriptor = new PluginDescriptor(
            "failed-plugin", "Failed", new Version(1, 0, 0),
            typeof(object).FullName!, "failed.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

        var loadResults = new List<PluginLoadResult>
        {
            new(descriptor, new FakePlugin(), null),
            new(failedDescriptor, null, new Exception("ロード失敗")),
        };

        var results = await loader.ExecutePluginsAndWaitAsync(
            loadResults, PluginStage.Processing, context);

        Assert.Equal(2, results.Count);
        Assert.Equal("ok-plugin", results[0].Descriptor.Id);
        Assert.True(results[0].Success);
        Assert.False(results[0].Skipped);

        Assert.Equal("failed-plugin", results[1].Descriptor.Id);
        Assert.True(results[1].Success);
        Assert.True(results[1].Skipped);
        Assert.Equal("ロードに失敗したためスキップされました。", results[1].SkipReason);
    }

    [Fact]
    public async Task ExecutePluginsAndWaitAsync_SkipsUnsupportedStage()
    {
        using var loader = new PluginLoader();
        var context = new PluginContext();

        var descriptor = new PluginDescriptor(
            "processing-only", "ProcessingOnly", new Version(1, 0, 0),
            typeof(object).FullName!, "processing.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

        var loadResults = new List<PluginLoadResult>
        {
            new(descriptor, new FakePlugin(), null),
        };

        var results = await loader.ExecutePluginsAndWaitAsync(
            loadResults, PluginStage.PreProcessing, context);

        Assert.Single(results);
        Assert.Equal("processing-only", results[0].Descriptor.Id);
        Assert.True(results[0].Success);
        Assert.True(results[0].Skipped);
        Assert.Contains("PreProcessing", results[0].SkipReason);
        Assert.Contains("対象外", results[0].SkipReason);
    }

    [Fact]
    public async Task ExecutePluginsAndWaitAsync_MixedSkippedAndExecuted()
    {
        using var loader = new PluginLoader();
        var context = new PluginContext();

        var preDescriptor = new PluginDescriptor(
            "pre-only", "PreOnly", new Version(1, 0, 0),
            typeof(object).FullName!, "pre.dll",
            new[] { PluginStage.PreProcessing }.ToFrozenSet());

        var procDescriptor = new PluginDescriptor(
            "proc-only", "ProcOnly", new Version(1, 0, 0),
            typeof(object).FullName!, "proc.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

        var bothDescriptor = new PluginDescriptor(
            "both", "Both", new Version(1, 0, 0),
            typeof(object).FullName!, "both.dll",
            new[] { PluginStage.PreProcessing, PluginStage.Processing }.ToFrozenSet());

        var loadResults = new List<PluginLoadResult>
        {
            new(preDescriptor, new FakePlugin(), null),
            new(procDescriptor, new FakePlugin(), null),
            new(bothDescriptor, new FakePlugin(), null),
        };

        var results = await loader.ExecutePluginsAndWaitAsync(
            loadResults, PluginStage.Processing, context);

        Assert.Equal(3, results.Count);
        Assert.Equal("pre-only", results[0].Descriptor.Id);
        Assert.True(results[0].Skipped);

        Assert.Equal("proc-only", results[1].Descriptor.Id);
        Assert.False(results[1].Skipped);
        Assert.True(results[1].Success);

        Assert.Equal("both", results[2].Descriptor.Id);
        Assert.False(results[2].Skipped);
        Assert.True(results[2].Success);
    }

    [Fact]
    public void ExecutePluginsAndWaitAsync_WithConfiguredGroups_UsesOrderedResultsGroups()
    {
        using var loader = new PluginLoader();
        SetLastConfig(loader, new PluginConfiguration
        {
            PluginsPath = "plugins",
            StageOrders =
            [
                new PluginStageOrderEntry
                {
                    Stage = PluginStage.Processing,
                    PluginOrder =
                    [
                        new PluginOrderEntry { Id = "plugin-b", Order = 1 },
                        new PluginOrderEntry { Id = "plugin-a", Order = 2 },
                        new PluginOrderEntry { Id = "missing", Order = 3 },
                    ]
                }
            ]
        });

        var loadResults = new List<PluginLoadResult>
        {
            new(CreateDescriptor("plugin-a"), new FakePlugin(), null),
            new(CreateDescriptor("plugin-b"), new FakePlugin(), null),
        };

        var groups = InvokeBuildExecutionGroupsForResults(loader, loadResults);

        Assert.NotNull(groups);
        Assert.Equal(2, groups!.Count);
        Assert.Equal(["plugin-b"], groups[0].Select(x => x.Descriptor.Id).ToArray());
        Assert.Equal(["plugin-a"], groups[1].Select(x => x.Descriptor.Id).ToArray());
    }

    [Fact]
    public void BuildExecutionGroupsForResults_WithDependencyOnlyConfiguration_ReturnsGroups()
    {
        using var loader = new PluginLoader();
        SetLastConfig(loader, new PluginConfiguration
        {
            PluginsPath = "plugins",
            PluginDependencies =
            [
                new PluginDependencyEntry { Id = "plugin-b", DependsOn = ["plugin-a"] },
            ]
        });

        var loadResults = new List<PluginLoadResult>
        {
            new(CreateDescriptor("plugin-a"), new FakePlugin(), null),
            new(CreateDescriptor("plugin-b"), new FakePlugin(), null),
        };

        var groups = InvokeBuildExecutionGroupsForResults(loader, loadResults);

        Assert.NotNull(groups);
        Assert.Single(groups!);
        Assert.Equal(["plugin-a", "plugin-b"], groups[0].Select(x => x.Descriptor.Id).ToArray());
    }

    [Fact]
    public void BuildExecutionGroupsForResults_WithEmptyConfiguration_ReturnsNull()
    {
        using var loader = new PluginLoader();
        SetLastConfig(loader, new PluginConfiguration { PluginsPath = "plugins" });

        var groups = InvokeBuildExecutionGroupsForResults(loader, [new PluginLoadResult(CreateDescriptor("plugin-a"), new FakePlugin(), null)]);

        Assert.Null(groups);
    }

    [Fact]
    public async Task ExecutePluginsAndWaitAsync_WithConfiguredButEmptyGroups_FallsBackToFlatExecution()
    {
        using var loader = new PluginLoader();
        var context = new PluginContext();
        SetLastConfig(loader, new PluginConfiguration
        {
            PluginsPath = "plugins",
            StageOrders =
            [
                new PluginStageOrderEntry
                {
                    Stage = PluginStage.Processing,
                    PluginOrder =
                    [
                        new PluginOrderEntry { Id = "missing", Order = 1 },
                    ]
                }
            ]
        });

        var loadResults = new List<PluginLoadResult>
        {
            new(CreateDescriptor("plugin-a"), new FakePlugin(), null),
        };

        var results = await loader.ExecutePluginsAndWaitAsync(loadResults, PluginStage.Processing, context);

        Assert.Single(results);
        Assert.True(results[0].Success);
        Assert.Equal("plugin-a", results[0].Descriptor.Id);
    }

    [Fact]
    public void ExecutePluginsAndWaitAsync_WithoutConfigurationGroups_ReturnsNullGroups()
    {
        using var loader = new PluginLoader();
        var groups = InvokeBuildExecutionGroupsForResults(loader, [new PluginLoadResult(CreateDescriptor("plugin-a"), new FakePlugin(), null)]);
        Assert.Null(groups);
    }

    [Fact]
    public void ResolveStageMaxDegreeOfParallelism_ClampsToHardLimit()
    {
        using var loader = new PluginLoader();
        SetLastConfig(loader, new PluginConfiguration
        {
            PluginsPath = "plugins",
            StageOrders =
            [
                new PluginStageOrderEntry
                {
                    Stage = PluginStage.Processing,
                    MaxDegreeOfParallelism = 999,
                }
            ]
        });

        var resolved = InvokeResolveStageMaxDegreeOfParallelism(loader, PluginStage.Processing);
        Assert.NotNull(resolved);
        Assert.InRange(resolved!.Value, 1, 32);
    }

    [Fact]
    public void GetRuntime_UnsupportedIsolationMode_ThrowsInvalidOperationException()
    {
        using var loader = new PluginLoader();
        var ex = Assert.Throws<TargetInvocationException>(() => InvokeGetRuntime(loader, (PluginIsolationMode)999));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("対応するランタイムが見つかりません", ex.InnerException!.Message);
    }

    [Fact]
    public void PluginMetadata_DefaultIsolationMode_IsInProcess()
    {
        var attribute = new PluginAttribute("test-id", "Test", "1.0.0");
        Assert.Equal(PluginIsolationMode.InProcess, attribute.IsolationMode);

        var descriptor = new PluginDescriptor(
            "test-id", "Test", new Version(1, 0, 0),
            typeof(object).FullName!, "test.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());
        Assert.Equal(PluginIsolationMode.InProcess, descriptor.IsolationMode);
    }

    [Fact]
    public async Task LoadPluginAsync_OutOfProcessIsolation_ReturnsErrorIfHostNotAvailable()
    {
        using var loader = new PluginLoader();
        var descriptor = new PluginDescriptor(
            "oop-plugin", "OutOfProcess", new Version(1, 0, 0),
            typeof(object).FullName!, "oop.dll",
            new[] { PluginStage.Processing }.ToFrozenSet())
        {
            IsolationMode = PluginIsolationMode.OutOfProcess,
        };

        var method = typeof(PluginLoader).GetMethod("LoadPluginAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task<PluginLoadResult>)method!.Invoke(loader, [descriptor, new PluginContext(), CancellationToken.None])!;
        var result = await task;

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.True(
            result.Error is FileNotFoundException or InvalidOperationException or TimeoutException or IOException,
            $"予期しない例外型: {result.Error.GetType().Name}");
    }

    private static PluginDescriptor CreateDescriptor(string id)
        => new(
            id,
            id,
            new Version(1, 0, 0),
            typeof(object).FullName!,
            $"{id}.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

    private static void SetLastConfig(PluginLoader loader, PluginConfiguration config)
    {
        var field = typeof(PluginLoader).GetField("_lastConfig", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(loader, config);
    }

    private static IReadOnlyList<IReadOnlyList<PluginLoadResult>>? InvokeBuildExecutionGroupsForResults(PluginLoader loader, IReadOnlyList<PluginLoadResult> loadResults)
    {
        var method = typeof(PluginLoader).GetMethod("BuildExecutionGroupsForResults", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (IReadOnlyList<IReadOnlyList<PluginLoadResult>>?)method!.Invoke(loader, [loadResults]);
    }

    private static int? InvokeResolveStageMaxDegreeOfParallelism(PluginLoader loader, PluginStage stage)
    {
        var method = typeof(PluginLoader).GetMethod("ResolveStageMaxDegreeOfParallelism", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (int?)method!.Invoke(loader, [stage]);
    }

    private static object? InvokeGetRuntime(PluginLoader loader, PluginIsolationMode isolationMode)
    {
        var method = typeof(PluginLoader).GetMethod("GetRuntime", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(loader, [isolationMode]);
    }

    private sealed class TestEventTracker : IPluginLoaderCallback
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
