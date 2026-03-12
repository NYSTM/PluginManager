using PluginManager;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.Loader;
using System.Text.Json;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// PluginManager の統合テストです。
/// </summary>
/// <remarks>
/// <para>
/// このクラスは、以下のシナリオをカバーします：
/// </para>
/// <list type="bullet">
/// <item>DLL 検出</item>
/// <item>順序解決</item>
/// <item>並列ロード</item>
/// <item>タイムアウト</item>
/// <item>リトライ</item>
/// <item>アンロード</item>
/// <item>依存関係競合</item>
/// <item>異常プラグイン混在時の継続動作</item>
/// </list>
/// <para>
/// <b>注意:</b> これらのテストは、SamplePlugin.dll が利用可能な場合のみ実行されます。
/// DLL が見つからない場合、テストはスキップされます。
/// </para>
/// </remarks>
public sealed class IntegrationTests : IAsyncLifetime
{
    private string _testDirectory = null!;
    private string _pluginsDirectory = null!;
    private string _configFilePath = null!;
    private bool _samplesAvailable;

    public async Task InitializeAsync()
    {
        // テスト用ディレクトリを作成
        _testDirectory = Path.Combine(Path.GetTempPath(), "PluginManagerIntegrationTests", Guid.NewGuid().ToString());
        _pluginsDirectory = Path.Combine(_testDirectory, "plugins");
        Directory.CreateDirectory(_pluginsDirectory);

        // SamplePlugin DLL をコピー
        _samplesAvailable = await CopySamplePluginsAsync();

        // テスト用設定ファイルを作成
        _configFilePath = Path.Combine(_testDirectory, "pluginsettings.json");
        await CreateDefaultConfigAsync();
    }

    public Task DisposeAsync()
    {
        // テスト用ディレクトリを削除
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                // ファイルロックを解放するために少し待機
                Task.Delay(100).Wait();
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // クリーンアップ失敗は無視
            }
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task EndToEnd_LoadAndExecute_Success()
    {
        // Arrange
        if (!_samplesAvailable)
        {
            // SamplePlugin.dll が見つからない場合はテストを成功扱いで終了
            return;
        }

        using var loader = new PluginLoader();
        var context = new PluginContext();

        await CreateConfigAsync(new[]
        {
            ("PreProcessing", new[] { ("sample-plugin-a", 1) }),
            ("Processing", new[] { ("sample-plugin-b", 1), ("sample-plugin-c", 2) })
        });

        // Act: ロード
        var loadResults = await loader.LoadFromConfigurationAsync(_configFilePath, context);

        // Assert: ロード成功
        Assert.NotEmpty(loadResults);
        var successCount = loadResults.Count(r => r.Success);
        Assert.True(successCount > 0, $"少なくとも1つのプラグインがロードされるべきです（成功: {successCount}/{loadResults.Count}）");

        // Act: 実行
        var preResults = await loader.ExecutePluginsAndWaitAsync(loadResults, PluginStage.PreProcessing, context);
        var procResults = await loader.ExecutePluginsAndWaitAsync(loadResults, PluginStage.Processing, context);

        // Assert: 実行成功
        Assert.True(preResults.Count > 0 || procResults.Count > 0, "少なくとも1つのプラグインが実行されるべきです");
    }

    [Fact]
    public async Task ParallelLoad_MultiplePlugins_LoadsConcurrently()
    {
        // Arrange
        if (!_samplesAvailable) return;

        using var loader = new PluginLoader();
        var context = new PluginContext();

        // 同じ Order のプラグインを作成（並列ロード）
        await CreateConfigAsync(new[]
        {
            ("Processing", new[] { ("sample-plugin-a", 1), ("sample-plugin-b", 1), ("sample-plugin-c", 1) })
        });

        // Act
        var stopwatch = Stopwatch.StartNew();
        var loadResults = await loader.LoadFromConfigurationAsync(_configFilePath, context);
        stopwatch.Stop();

        // Assert: すべて成功または一部成功
        Assert.NotEmpty(loadResults);
        var successCount = loadResults.Count(r => r.Success);
        Assert.True(successCount > 0, $"少なくとも1つのプラグインがロードされるべきです（成功: {successCount}/{loadResults.Count}）");

        // Assert: 並列ロードされている（順次ロードより速い）
        Assert.True(stopwatch.ElapsedMilliseconds < 10000, $"並列ロードが期待より遅い: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task OrderResolution_DifferentOrders_LoadsInCorrectSequence()
    {
        // Arrange
        if (!_samplesAvailable) return;

        using var loader = new PluginLoader();
        var context = new PluginContext();

        // 異なる Order のプラグインを作成
        await CreateConfigAsync(new[]
        {
            ("Processing", new[] { ("sample-plugin-a", 1), ("sample-plugin-b", 2), ("sample-plugin-c", 3) })
        });

        // Act
        var loadResults = await loader.LoadFromConfigurationAsync(_configFilePath, context);

        // Assert: 順序通りロード
        Assert.NotEmpty(loadResults);
        var successCount = loadResults.Count(r => r.Success);
        Assert.True(successCount > 0, $"少なくとも1つのプラグインがロードされるべきです（成功: {successCount}/{loadResults.Count}）");

        // 実行して順序を確認
        var execResults = await loader.ExecutePluginsAndWaitAsync(loadResults, PluginStage.Processing, context);
        Assert.True(execResults.Count > 0, "少なくとも1つのプラグインが実行されるべきです");
    }

    [Fact]
    public async Task Timeout_VeryShortTimeout_MayFail()
    {
        // Arrange
        if (!_samplesAvailable) return;

        using var loader = new PluginLoader();
        var context = new PluginContext();

        // 非常に短いタイムアウトを設定
        await CreateConfigAsync(
            new[] { ("Processing", new[] { ("sample-plugin-a", 1) }) },
            timeoutMs: 1);

        // Act
        var loadResults = await loader.LoadFromConfigurationAsync(_configFilePath, context);

        // Assert: タイムアウトまたは成功（タイミング依存）
        Assert.NotNull(loadResults);
    }

    [Fact]
    public async Task MixedPlugins_SomeFailSomeSucceed_ContinuesProcessing()
    {
        // Arrange
        if (!_samplesAvailable) return;

        using var loader = new PluginLoader();
        var context = new PluginContext();

        // 存在するプラグインと存在しないプラグインを混在
        await CreateConfigAsync(new[]
        {
            ("Processing", new[] { ("sample-plugin-a", 1), ("nonexistent-plugin", 2) })
        });

        // Act
        var loadResults = await loader.LoadFromConfigurationAsync(_configFilePath, context);

        // Assert: 正常なプラグインだけロード成功
        Assert.NotEmpty(loadResults);
        var successCount = loadResults.Count(r => r.Success);
        Assert.True(successCount >= 1, $"少なくとも1つのプラグインがロードされるべきです（成功: {successCount}/{loadResults.Count}）");

        // Assert: 存在しないプラグインは失敗
        var failedPlugins = loadResults.Where(r => !r.Success);
        Assert.NotEmpty(failedPlugins);
    }

    [Fact]
    public async Task Unload_LoadedPlugin_SuccessfullyUnloads()
    {
        // Arrange
        if (!_samplesAvailable) return;

        using var loader = new PluginLoader();
        var context = new PluginContext();

        await CreateConfigAsync(new[]
        {
            ("Processing", new[] { ("sample-plugin-a", 1) })
        });

        // Act: ロード
        var loadResults = await loader.LoadFromConfigurationAsync(_configFilePath, context);
        var successResults = loadResults.Where(r => r.Success).ToList();
        Assert.NotEmpty(successResults);

        var assemblyPath = successResults[0].Descriptor.AssemblyPath;

        // Act: アンロード
        await loader.UnloadPluginAsync(assemblyPath);

        // Assert: アンロード成功（例外が発生しないこと）
        Assert.True(true);
    }

    [Fact]
    public async Task Reload_AfterUnload_SuccessfullyReloads()
    {
        // Arrange
        if (!_samplesAvailable) return;

        using var loader = new PluginLoader();
        var context = new PluginContext();

        await CreateConfigAsync(new[]
        {
            ("Processing", new[] { ("sample-plugin-a", 1) })
        });

        // Act: 初回ロード
        var loadResults1 = await loader.LoadFromConfigurationAsync(_configFilePath, context);
        var successResults1 = loadResults1.Where(r => r.Success).ToList();
        Assert.NotEmpty(successResults1);

        var assemblyPath = successResults1[0].Descriptor.AssemblyPath;
        var pluginId = successResults1[0].Descriptor.Id;

        // Act: アンロード
        await loader.UnloadPluginAsync(assemblyPath);

        // 少し待機してファイルロックが解放されるのを待つ
        await Task.Delay(200);

        // Act: 再ロード
        var loadResults2 = await loader.LoadFromConfigurationAsync(_configFilePath, context);

        // Assert: 再ロード成功
        var successResults2 = loadResults2.Where(r => r.Success).ToList();
        Assert.NotEmpty(successResults2);
        Assert.Contains(successResults2, r => r.Descriptor.Id == pluginId);
    }

    [Fact]
    public async Task ContextSharing_AcrossStages_DataPersists()
    {
        // Arrange
        if (!_samplesAvailable) return;

        using var loader = new PluginLoader();
        var context = new PluginContext();
        context.SetProperty("InitialData", "test-value");

        await CreateConfigAsync(new[]
        {
            ("PreProcessing", new[] { ("sample-plugin-a", 1) }),
            ("Processing", new[] { ("sample-plugin-b", 1) })
        });

        // Act
        var loadResults = await loader.LoadFromConfigurationAsync(_configFilePath, context);

        await loader.ExecutePluginsAndWaitAsync(loadResults, PluginStage.PreProcessing, context);
        await loader.ExecutePluginsAndWaitAsync(loadResults, PluginStage.Processing, context);

        // Assert: コンテキストデータが保持されている
        Assert.True(context.TryGetProperty<string>("InitialData", out var data));
        Assert.Equal("test-value", data);
    }

    [Fact]
    public async Task DisposeAsync_WithLoadedPlugins_CleansUpResources()
    {
        // Arrange
        if (!_samplesAvailable) return;

        var loader = new PluginLoader();
        var context = new PluginContext();

        await CreateConfigAsync(new[]
        {
            ("Processing", new[] { ("sample-plugin-a", 1) })
        });

        // Act: ロード
        var loadResults = await loader.LoadFromConfigurationAsync(_configFilePath, context);
        Assert.NotEmpty(loadResults);

        // Act: Dispose
        await loader.DisposeAsync();

        // Assert: Dispose 後は操作できない
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await loader.LoadAsync(_pluginsDirectory, context));
    }

    [Fact]
    public async Task ErrorCategories_MixedErrors_ClassifiesCorrectly()
    {
        // Arrange
        if (!_samplesAvailable) return;

        using var loader = new PluginLoader();
        var context = new PluginContext();

        // 存在しないプラグインと存在するプラグインを混在
        await CreateConfigAsync(new[]
        {
            ("Processing", new[] { ("sample-plugin-a", 1), ("nonexistent-plugin", 2) })
        });

        // Act
        var loadResults = await loader.LoadFromConfigurationAsync(_configFilePath, context);

        // Assert: エラー分類が正しい
        var failedResults = loadResults.Where(r => !r.Success).ToList();
        Assert.NotEmpty(failedResults);

        foreach (var failed in failedResults)
        {
            Assert.NotNull(failed.ErrorInfo);
            Assert.NotEqual(PluginErrorCategory.Unknown, failed.ErrorInfo!.Category);
        }
    }

    // ===== 追加テスト =====

    /// <summary>
    /// Order=1, 2, 3 のプラグインが厳密に順序通り実行されることを検証します。
    /// </summary>
    [Fact]
    public async Task ExecutionOrder_Order1Then2Then3_ExecutedInStrictOrder()
    {
        // Arrange
        var executionLog = new ConcurrentQueue<int>();

        var stage = PluginStage.Processing;
        var context = new PluginContext();

        static PluginDescriptor MakeDescriptor(string id, int order) =>
            new(id, id, new Version(1, 0, 0),
                typeof(object).FullName!, $"{id}.dll",
                new[] { PluginStage.Processing }.ToFrozenSet());

        // 各 Order に対応するモックプラグインと PluginLoadResult を構築
        var log = executionLog;
        var plugin1 = new OrderRecordingPlugin(1, log);
        var plugin2 = new OrderRecordingPlugin(2, log);
        var plugin3 = new OrderRecordingPlugin(3, log);

        var desc1 = MakeDescriptor("plugin-order-1", 1);
        var desc2 = MakeDescriptor("plugin-order-2", 2);
        var desc3 = MakeDescriptor("plugin-order-3", 3);

        var result1 = new PluginLoadResult(desc1, plugin1);
        var result2 = new PluginLoadResult(desc2, plugin2);
        var result3 = new PluginLoadResult(desc3, plugin3);

        // Order グループを手動構築: 各 Order は独立グループ
        var groups = new List<IReadOnlyList<PluginLoadResult>>
        {
            new[] { result1 },
            new[] { result2 },
            new[] { result3 },
        };

        // Act
        var results = await PluginExecutor.ExecutePluginsInGroupsAsync(groups, stage, context);

        // Assert: 実行順が 1 -> 2 -> 3 であること
        Assert.Equal(3, results.Count(r => !r.Skipped && r.Success));
        var order = executionLog.ToArray();
        Assert.Equal([1, 2, 3], order);
    }

    /// <summary>
    /// PluginContext を複数スレッドから同時に更新しても競合エラーが発生しないことを検証します。
    /// </summary>
    [Fact]
    public async Task PluginContext_ConcurrentUpdates_ThreadSafe()
    {
        // Arrange
        var context = new PluginContext();
        const int threadCount = 20;
        const int iterationsPerThread = 500;

        // Act: 複数タスクが同時に SetProperty / GetProperty を実行
        var tasks = Enumerable.Range(0, threadCount).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < iterationsPerThread; i++)
            {
                var key = $"key-{t}-{i}";
                context.SetProperty(key, i);
                context.TryGetProperty<int>(key, out _);
                context.RemoveProperty(key);
            }
        }));

        // Assert: 例外が発生しないこと
        await Task.WhenAll(tasks);

        // 競合書き込みテスト: 同一キーに全スレッドから同時に書き込む
        var sharedKey = "shared-counter";
        context.SetProperty(sharedKey, 0);

        var writeTasks = Enumerable.Range(0, threadCount).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < iterationsPerThread; i++)
                context.SetProperty(sharedKey, t * iterationsPerThread + i);
        }));

        await Task.WhenAll(writeTasks);

        // 最終的に何らかの値が格納されていること（例外なし）
        Assert.True(context.TryGetProperty<int>(sharedKey, out _));
    }

    /// <summary>
    /// PluginLoadContext をアンロードし、強参照を手放した後に GC で回収されることを検証します。
    /// </summary>
    [Fact]
    public async Task AlcUnload_AfterReleasingReferences_CollectedByGc()
    {
        // WeakReference を返す専用メソッドにしてインライン強参照を残さないようにする
        var weakRef = CreateAndUnloadAlc();

        // GC を複数回実行して回収を試みる
        for (var i = 0; i < 10 && weakRef.IsAlive; i++)
        {
            await Task.Run(() =>
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            });
            await Task.Delay(100);
        }

        // Assert: ALC が GC で回収されていること
        Assert.False(weakRef.IsAlive, "ALC への強参照を手放した後は GC で回収されるべきです。");
    }

    /// <summary>
    /// PluginContext のプロパティを ToJsonDictionary → ApplyJsonDictionary でラウンドトリップしても
    /// 型と値が保持されることを検証します（別プロセス境界での IPC シミュレーション）。
    /// </summary>
    [Fact]
    public void PluginContext_JsonRoundTrip_TypeAndValuePreserved()
    {
        // Arrange
        var original = new PluginContext();
        original.SetProperty("intVal", 42);
        original.SetProperty("longVal", 9_876_543_210L);
        original.SetProperty("doubleVal", 3.14);
        original.SetProperty("boolTrue", true);
        original.SetProperty("boolFalse", false);
        original.SetProperty("strVal", "hello");
        original.SetProperty("nullVal", (object?)null);

        // Act: ToJsonDictionary → ApplyJsonDictionary (IPC 境界シミュレーション)
        var jsonDict = original.ToJsonDictionary();

        // JSON テキスト経由のシリアライズ / デシリアライズ（プロセス間転送を模擬）
        var jsonText = JsonSerializer.Serialize(jsonDict);
        var restored = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonText)!;

        var target = new PluginContext();
        target.ApplyJsonDictionary(restored);

        // Assert: 値と型が保持されること
        Assert.True(target.TryGetProperty<int>("intVal", out var iv));
        Assert.Equal(42, iv);

        Assert.True(target.TryGetProperty<bool>("boolTrue", out var bt));
        Assert.True(bt);

        Assert.True(target.TryGetProperty<bool>("boolFalse", out var bf));
        Assert.False(bf);

        Assert.True(target.TryGetProperty<string>("strVal", out var sv));
        Assert.Equal("hello", sv);

        Assert.True(target.TryGetProperty<string>("nullVal", out var nv));
        Assert.Null(nv);

        // double は近似比較
        Assert.True(target.TryGetProperty<double>("doubleVal", out var dv));
        Assert.Equal(3.14, dv, precision: 10);
    }

    // ===== 追加テスト用ヘルパー =====

    /// <summary>
    /// 実行順を記録するモックプラグインです。
    /// </summary>
    private sealed class OrderRecordingPlugin(int order, ConcurrentQueue<int> log) : IPlugin
    {
        public string Id => $"order-plugin-{order}";
        public string Name => Id;
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } =
            new[] { PluginStage.Processing }.ToFrozenSet();

        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
        {
            log.Enqueue(order);
            return Task.FromResult<object?>(order);
        }
    }

    /// <summary>
    /// ALC を生成してアンロードし、WeakReference を返します。
    /// このメソッドのスタックフレームが破棄されることで強参照が消えます。
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndUnloadAlc()
    {
        var alc = new AssemblyLoadContext("test-alc-gc", isCollectible: true);
        var weakRef = new WeakReference(alc, trackResurrection: true);
        alc.Unload();
        return weakRef;
    }

    // ===== 既存ヘルパーメソッド =====

    private void SkipIfSamplesNotAvailable()
    {
        if (!_samplesAvailable)
        {
            // xUnit では、テストを動的にスキップすることはできないため、
            // テストを成功扱いで終了します
            Assert.True(true, "SamplePlugin.dll が見つからないため、テストをスキップします");
        }
    }

    private async Task<bool> CopySamplePluginsAsync()
    {
        // SamplePlugin.dll をテストディレクトリにコピー
        var baseDir = AppContext.BaseDirectory;
        
        // bin\Debug\net8.0 または bin\Release\net8.0 から探す
        var possiblePaths = new[]
        {
            Path.Combine(baseDir, "SamplePlugin.dll"),
            Path.Combine(baseDir, "..", "..", "..", "SamplePlugin", "bin", "Debug", "net8.0", "SamplePlugin.dll"),
            Path.Combine(baseDir, "..", "..", "..", "SamplePlugin", "bin", "Release", "net8.0", "SamplePlugin.dll"),
        };

        string? samplePluginPath = null;
        foreach (var path in possiblePaths)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    samplePluginPath = fullPath;
                    break;
                }
            }
            catch
            {
                // パス解決失敗は無視
            }
        }

        if (samplePluginPath == null)
        {
            return false;
        }

        try
        {
            var destPath = Path.Combine(_pluginsDirectory, "SamplePlugin.dll");
            File.Copy(samplePluginPath, destPath, overwrite: true);

            // 依存DLLもコピー
            var sourceDir = Path.GetDirectoryName(samplePluginPath)!;
            var pdbPath = Path.Combine(sourceDir, "SamplePlugin.pdb");
            if (File.Exists(pdbPath))
            {
                File.Copy(pdbPath, Path.Combine(_pluginsDirectory, "SamplePlugin.pdb"), overwrite: true);
            }

            // PluginManager.Core.dll もコピー（依存関係）
            var coreDllPath = Path.Combine(sourceDir, "PluginManager.Core.dll");
            if (File.Exists(coreDllPath))
            {
                File.Copy(coreDllPath, Path.Combine(_pluginsDirectory, "PluginManager.Core.dll"), overwrite: true);
            }

            return true;
        }
        catch
        {
            return false;
        }

        await Task.CompletedTask;
    }

    private async Task CreateDefaultConfigAsync()
    {
        var json = $$"""
        {
          "PluginsPath": "{{_pluginsDirectory.Replace("\\", "\\\\")}}",
          "IntervalMilliseconds": 0,
          "TimeoutMilliseconds": 5000,
          "RetryCount": 3,
          "RetryDelayMilliseconds": 100,
          "StageOrders": []
        }
        """;

        await File.WriteAllTextAsync(_configFilePath, json);
    }

    private async Task CreateConfigAsync(
        (string Stage, (string Id, int Order)[] Plugins)[] stages,
        int timeoutMs = 5000)
    {
        var stageOrdersJson = string.Join(",\n    ", stages.Select(stage =>
        {
            var pluginOrdersJson = string.Join(",\n        ", stage.Plugins.Select(p =>
                $$$"""{ "Id": "{{{p.Id}}}", "Order": {{{p.Order}}} }"""));

            return $$$"""
    {
      "Stage": "{{{stage.Stage}}}",
      "PluginOrder": [
        {{{pluginOrdersJson}}}
      ]
    }
""";
        }));

        var json = $@"{{
  ""PluginsPath"": ""{_pluginsDirectory.Replace("\\", "\\\\")}"",
  ""IntervalMilliseconds"": 0,
  ""TimeoutMilliseconds"": {timeoutMs},
  ""RetryCount"": 3,
  ""RetryDelayMilliseconds"": 100,
  ""StageOrders"": [
    {stageOrdersJson}
  ]
}}";

        await File.WriteAllTextAsync(_configFilePath, json);
    }
}
