using System.Collections.Frozen;
using System.Diagnostics;
using PluginManager;
using Xunit;

namespace PluginManagerTest;


/// <summary>
/// リソース健全性と反復安定性の監視テストです。
/// </summary>
public sealed class PluginResourceHealthTests : IAsyncLifetime
{
    private string _testDirectory = null!;
    private string _pluginsDirectory = null!;
    private string _configFilePath = null!;
    private bool _samplesAvailable;

    public async Task InitializeAsync()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "PluginResourceHealthTests", Guid.NewGuid().ToString("N"));
        _pluginsDirectory = Path.Combine(_testDirectory, "plugins");
        _configFilePath = Path.Combine(_testDirectory, "pluginsettings.json");
        Directory.CreateDirectory(_pluginsDirectory);

        _samplesAvailable = TryCopySamplePlugin();
        await CreateConfigAsync();
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // テストディレクトリの削除失敗は無視
            }
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task LoadAndUnload_Repeatedly_DoesNotGrowMemoryExcessively()
    {
        if (!_samplesAvailable)
            return;

        ForceCollect();
        var before = GC.GetTotalMemory(forceFullCollection: true);

        for (var i = 0; i < 20; i++)
        {
            using var loader = new PluginLoader();
            var context = new PluginContext();
            var results = await loader.LoadFromConfigurationAsync(_configFilePath, context);

            foreach (var result in results.Where(x => x.Success))
                await loader.UnloadPluginAsync(result.Descriptor.AssemblyPath);
        }

        ForceCollect();
        var after = GC.GetTotalMemory(forceFullCollection: true);
        var growth = after - before;

        Assert.True(growth < 50 * 1024 * 1024, $"メモリ増分が大きすぎます: {growth} bytes");
    }

    [Fact]
    public async Task LoadAndUnload_Repeatedly_LeavesNoPluginHostProcesses()
    {
        if (!_samplesAvailable)
            return;

        var beforeIds = Process.GetProcessesByName("PluginHost").Select(p => p.Id).ToHashSet();

        for (var i = 0; i < 5; i++)
        {
            using var loader = new PluginLoader();
            var context = new PluginContext();
            var results = await loader.LoadFromConfigurationAsync(_configFilePath, context);

            foreach (var result in results.Where(x => x.Success && x.Descriptor.IsolationMode == PluginIsolationMode.OutOfProcess))
                await loader.UnloadPluginAsync(result.Descriptor.AssemblyPath);
        }

        await WaitForPluginHostExitAsync(beforeIds);

        var afterIds = Process.GetProcessesByName("PluginHost")
            .Where(p => !beforeIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToArray();

        Assert.Empty(afterIds);
    }

    [Fact]
    public async Task LoadExecuteUnload_SoakTest_RemainsStable()
    {
        if (!_samplesAvailable)
            return;

        for (var i = 0; i < 20; i++)
        {
            using var loader = new PluginLoader();
            var context = new PluginContext();
            var results = await loader.LoadFromConfigurationAsync(_configFilePath, context);
            Assert.Contains(results, result => result.Success);

            var executionResults = await loader.ExecutePluginsAndWaitAsync(results, PluginStage.Processing, context);
            Assert.NotEmpty(executionResults);

            foreach (var result in results.Where(x => x.Success))
                await loader.UnloadPluginAsync(result.Descriptor.AssemblyPath);
        }
    }

    [Fact]
    public async Task LoadAndUnload_OutOfProcessRuntime_LeavesNoPluginHostProcesses()
    {
        var beforeIds = Process.GetProcessesByName("PluginHost").Select(p => p.Id).ToHashSet();
        using var runtime = new OutOfProcessPluginRuntime();
        var descriptor = new PluginDescriptor(
            "oop-monitoring-plugin",
            "OutOfProcessMonitoringPlugin",
            new Version(1, 0, 0),
            typeof(OutOfProcessMonitoringPlugin).FullName!,
            CreateIsolatedPluginAssemblyPath(),
            new[] { PluginStage.Processing }.ToFrozenSet())
        {
            IsolationMode = PluginIsolationMode.OutOfProcess,
        };

        var result = await runtime.LoadAsync(descriptor, new PluginContext(), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);

        var targetHostIds = Process.GetProcessesByName("PluginHost")
            .Select(p => p.Id)
            .Where(id => !beforeIds.Contains(id))
            .ToArray();

        Assert.Single(targetHostIds);

        await runtime.UnloadAsync(descriptor.AssemblyPath);
        await WaitForPluginHostExitAsync(targetHostIds);

        var remainingTargetHostIds = targetHostIds
            .Where(IsProcessAlive)
            .ToArray();

        Assert.Empty(remainingTargetHostIds);
    }

    private static void ForceCollect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static async Task WaitForPluginHostExitAsync(IEnumerable<int> processIds)
    {
        var targetIds = processIds.Distinct().ToArray();
        if (targetIds.Length == 0)
            return;

        for (var i = 0; i < 20; i++)
        {
            if (targetIds.All(id => !IsProcessAlive(id)))
                return;

            await Task.Delay(200);
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private bool TryCopySamplePlugin()
    {
        var samplePluginPath = ResolveSamplePluginPath();
        if (samplePluginPath is null)
            return false;

        try
        {
            var sourceDir = Path.GetDirectoryName(samplePluginPath)!;
            File.Copy(samplePluginPath, Path.Combine(_pluginsDirectory, "SamplePlugin.dll"), overwrite: true);

            var dependencies = new[]
            {
                "PluginManager.Core.dll",
                "PluginManager.dll",
                "PluginManager.Ipc.dll"
            };

            foreach (var dependency in dependencies)
            {
                var sourcePath = Path.Combine(sourceDir, dependency);
                if (File.Exists(sourcePath))
                    File.Copy(sourcePath, Path.Combine(_pluginsDirectory, dependency), overwrite: true);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveSamplePluginPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var possiblePaths = new[]
        {
            Path.Combine(baseDir, "SamplePlugin.dll"),
            Path.Combine(baseDir, "..", "..", "..", "SamplePlugin", "bin", "Debug", "net8.0-windows", "SamplePlugin.dll"),
            Path.Combine(baseDir, "..", "..", "..", "SamplePlugin", "bin", "Release", "net8.0-windows", "SamplePlugin.dll"),
            Path.Combine(baseDir, "..", "..", "..", "SamplePlugin", "bin", "Debug", "net8.0", "SamplePlugin.dll"),
            Path.Combine(baseDir, "..", "..", "..", "SamplePlugin", "bin", "Release", "net8.0", "SamplePlugin.dll"),
        };

        foreach (var path in possiblePaths)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                    return fullPath;
            }
            catch
            {
                // パス解決失敗は無視
            }
        }

        return null;
    }

    private Task CreateConfigAsync()
    {
        var json = $$"""
        {
          "PluginsPath": "{{_pluginsDirectory.Replace("\\", "\\\\")}}",
          "IntervalMilliseconds": 0,
          "TimeoutMilliseconds": 5000,
          "RetryCount": 2,
          "RetryDelayMilliseconds": 100,
          "StageOrders": [
            {
              "Stage": "Processing",
              "PluginOrder": [
                { "Id": "sample-plugin-a", "Order": 1 },
                { "Id": "sample-plugin-b", "Order": 1 },
                { "Id": "sample-plugin-c", "Order": 1 }
              ]
            }
          ]
        }
        """;

        return File.WriteAllTextAsync(_configFilePath, json);
    }

    private static string CreateIsolatedPluginAssemblyPath()
    {
        var sourceAssemblyPath = typeof(PluginResourceHealthTests).Assembly.Location;
        var tempDirectory = Path.Combine(Path.GetTempPath(), "PluginManagerTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        var copiedAssemblyPath = Path.Combine(tempDirectory, Path.GetFileName(sourceAssemblyPath));
        File.Copy(sourceAssemblyPath, copiedAssemblyPath, overwrite: true);
        return copiedAssemblyPath;
    }
}
