using System.Diagnostics;
using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="PluginLoader"/> の通常時レスポンス監視テストです。
/// </summary>
public sealed class PluginLoaderPerformanceTests : IAsyncLifetime
{
    private string _testDirectory = null!;
    private string _pluginsDirectory = null!;
    private string _configFilePath = null!;
    private bool _samplesAvailable;

    public async Task InitializeAsync()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "PluginLoaderPerformanceTests", Guid.NewGuid().ToString("N"));
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
    public async Task LoadFromConfigurationAsync_WithNormalConfiguration_CompletesWithinSoftBaseline()
    {
        if (!_samplesAvailable)
            return;

        using var warmupLoader = new PluginLoader();
        var warmupContext = new PluginContext();
        _ = await warmupLoader.LoadFromConfigurationAsync(_configFilePath, warmupContext);

        using var loader = new PluginLoader();
        var context = new PluginContext();
        var stopwatch = Stopwatch.StartNew();

        var results = await loader.LoadFromConfigurationAsync(_configFilePath, context);

        stopwatch.Stop();
        Assert.NotEmpty(results);
        Assert.Contains(results, result => result.Success);
        Assert.True(stopwatch.ElapsedMilliseconds < 10000, $"通常時ロードが遅すぎます: {stopwatch.ElapsedMilliseconds}ms");
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

            var coreDllPath = Path.Combine(sourceDir, "PluginManager.Core.dll");
            if (File.Exists(coreDllPath))
                File.Copy(coreDllPath, Path.Combine(_pluginsDirectory, "PluginManager.Core.dll"), overwrite: true);

            var pdbPath = Path.Combine(sourceDir, "SamplePlugin.pdb");
            if (File.Exists(pdbPath))
                File.Copy(pdbPath, Path.Combine(_pluginsDirectory, "SamplePlugin.pdb"), overwrite: true);

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
}
