using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="PluginConfigurationLoader"/> のテストです。
/// </summary>
public sealed class PluginConfigurationLoaderTests
{
    [Fact]
    public void Load_EmptyPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => PluginConfigurationLoader.Load(string.Empty));
    }

    [Fact]
    public void Load_MissingFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");
        Assert.Throws<FileNotFoundException>(() => PluginConfigurationLoader.Load(path));
    }

    [Fact]
    public void Load_NullJson_ThrowsInvalidOperationException()
    {
        var path = CreateTempConfig("null");

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => PluginConfigurationLoader.Load(path));
            Assert.Contains("読み込みに失敗", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_NegativeRetryCount_ThrowsInvalidOperationException()
    {
        var path = CreateTempConfig("""
        {
          "PluginsPath": "plugins",
          "RetryCount": -1,
          "StageOrders": []
        }
        """);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => PluginConfigurationLoader.Load(path));
            Assert.Contains("RetryCount", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_NegativeRetryDelayMilliseconds_ThrowsInvalidOperationException()
    {
        var path = CreateTempConfig("""
        {
          "PluginsPath": "plugins",
          "RetryDelayMilliseconds": -1,
          "StageOrders": []
        }
        """);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => PluginConfigurationLoader.Load(path));
            Assert.Contains("RetryDelayMilliseconds", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_StageOrdersNull_ThrowsInvalidOperationException()
    {
        var path = CreateTempConfig("""
        {
          "PluginsPath": "plugins",
          "StageOrders": null
        }
        """);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => PluginConfigurationLoader.Load(path));
            Assert.Contains("StageOrders", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_NullStageOrderEntry_ThrowsInvalidOperationException()
    {
        var path = CreateTempConfig("""
        {
          "PluginsPath": "plugins",
          "StageOrders": [ null ]
        }
        """);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => PluginConfigurationLoader.Load(path));
            Assert.Contains("StageOrders[0]", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_NullStage_ThrowsInvalidOperationException()
    {
        var path = CreateTempConfig("""
        {
          "PluginsPath": "plugins",
          "StageOrders": [
            {
              "Stage": null,
              "PluginOrder": []
            }
          ]
        }
        """);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => PluginConfigurationLoader.Load(path));
            Assert.Contains("StageOrders[0].Stage", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_NullPluginOrder_ThrowsInvalidOperationException()
    {
        var path = CreateTempConfig("""
        {
          "PluginsPath": "plugins",
          "StageOrders": [
            {
              "Stage": "Processing",
              "PluginOrder": null
            }
          ]
        }
        """);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => PluginConfigurationLoader.Load(path));
            Assert.Contains("PluginOrder", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MaxDegreeOfParallelismZero_ThrowsInvalidOperationException()
    {
        var path = CreateTempConfig("""
        {
          "PluginsPath": "plugins",
          "StageOrders": [
            {
              "Stage": "Processing",
              "MaxDegreeOfParallelism": 0,
              "PluginOrder": []
            }
          ]
        }
        """);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => PluginConfigurationLoader.Load(path));
            Assert.Contains("MaxDegreeOfParallelism", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_WhitespacePluginId_ThrowsInvalidOperationException()
    {
        var path = CreateTempConfig("""
        {
          "PluginsPath": "plugins",
          "StageOrders": [
            {
              "Stage": "Processing",
              "PluginOrder": [
                { "Id": "   ", "Order": 1 }
              ]
            }
          ]
        }
        """);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => PluginConfigurationLoader.Load(path));
            Assert.Contains("PluginOrder[0].Id", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_DuplicatePluginIdsInStage_ThrowsInvalidOperationException()
    {
        var path = CreateTempConfig("""
        {
          "PluginsPath": "plugins",
          "StageOrders": [
            {
              "Stage": "Processing",
              "PluginOrder": [
                { "Id": "plugin-a", "Order": 1 },
                { "Id": "PLUGIN-A", "Order": 2 }
              ]
            }
          ]
        }
        """);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => PluginConfigurationLoader.Load(path));
            Assert.Contains("重複", ex.Message);
            Assert.Contains("plugin-a", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RelativePluginsPath_ResolvesAgainstConfigDirectory()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var configPath = Path.Combine(tempDirectory, "pluginsettings.json");
        File.WriteAllText(configPath, """
        {
          "PluginsPath": "plugins",
          "StageOrders": []
        }
        """);

        try
        {
            var config = PluginConfigurationLoader.Load(configPath);
            Assert.Equal(Path.Combine(tempDirectory, "plugins"), config.PluginsPath);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static string CreateTempConfig(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return path;
    }
}
