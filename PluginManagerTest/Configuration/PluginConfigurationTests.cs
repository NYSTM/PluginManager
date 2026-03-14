using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// PluginConfiguration のユニットテスト
/// </summary>
public sealed class PluginConfigurationTests
{
    /// <summary>
    /// 正常な設定ファイルを読み込めることを確認します。
    /// </summary>
    [Fact]
    public void Load_ValidFile_ReturnsConfiguration()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var json = """
        {
          "PluginsPath": "test-plugins",
          "IntervalMilliseconds": 300,
          "TimeoutMilliseconds": 4000,
          "StageOrders": [
            {
              "Stage": "前処理",
              "PluginOrder": [
                { "Id": "TestPluginA", "Order": 1 }
              ]
            },
            {
              "Stage": "後処理",
              "PluginOrder": [
                { "Id": "TestPluginB", "Order": 1 }
              ]
            }
          ]
        }
        """;
        File.WriteAllText(tempFile, json);

        try
        {
            // Act
            var config = PluginConfiguration.Load(tempFile);

            // Assert
            Assert.NotNull(config);
            var expectedPluginsPath = Path.GetFullPath("test-plugins", Path.GetDirectoryName(tempFile)!);
            Assert.Equal(expectedPluginsPath, config.PluginsPath);
            Assert.Equal(300, config.IntervalMilliseconds);
            Assert.Equal(4000, config.TimeoutMilliseconds);
            Assert.Equal(2, config.StageOrders.Count);

            var preOrder = config.GetPluginOrder(new PluginStage("前処理"));
            Assert.Single(preOrder);
            Assert.Equal("TestPluginA", preOrder[0].Id);
            Assert.Equal(1, preOrder[0].Order);

            var postOrder = config.GetPluginOrder(new PluginStage("後処理"));
            Assert.Single(postOrder);
            Assert.Equal("TestPluginB", postOrder[0].Id);
            Assert.Equal(1, postOrder[0].Order);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// 存在しない設定ファイル指定時に例外が発生することを確認します。
    /// </summary>
    [Fact]
    public void Load_FileNotFound_ThrowsException()
    {
        // Arrange
        var nonExistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => PluginConfiguration.Load(nonExistentFile));
    }

    /// <summary>
    /// 空文字または未指定パス時に例外が発生することを確認します。
    /// </summary>
    [Fact]
    public void Load_EmptyPath_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => PluginConfiguration.Load(string.Empty));
        Assert.Throws<ArgumentException>(() => PluginConfiguration.Load(null!));
    }

    /// <summary>
    /// 最小構成の設定ファイルで既定値が適用されることを確認します。
    /// </summary>
    [Fact]
    public void Load_MinimalConfiguration_UsesDefaultValues()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var json = """
        {
          "PluginsPath": "plugins"
        }
        """;
        File.WriteAllText(tempFile, json);

        try
        {
            // Act
            var config = PluginConfiguration.Load(tempFile);

            // Assert
            Assert.NotNull(config);
            var expectedPluginsPath = Path.GetFullPath("plugins", Path.GetDirectoryName(tempFile)!);
            Assert.Equal(expectedPluginsPath, config.PluginsPath);
            Assert.Equal(0, config.IntervalMilliseconds);
            Assert.Equal(0, config.TimeoutMilliseconds);
            Assert.Empty(config.StageOrders);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// RetryCount / RetryDelayMilliseconds が正しく読み込まれることを確認します。
    /// </summary>
    [Fact]
    public void Load_WithRetrySettings_ReturnsRetryConfiguration()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var json = """
        {
          "PluginsPath": "plugins",
          "RetryCount": 3,
          "RetryDelayMilliseconds": 500
        }
        """;
        File.WriteAllText(tempFile, json);

        try
        {
            // Act
            var config = PluginConfiguration.Load(tempFile);

            // Assert
            Assert.Equal(3, config.RetryCount);
            Assert.Equal(500, config.RetryDelayMilliseconds);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// RetryCount 未指定時の既定値が 0 であることを確認します。
    /// </summary>
    [Fact]
    public void Load_MinimalConfiguration_RetryCountDefaultsToZero()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """{ "PluginsPath": "plugins" }""");

        try
        {
            var config = PluginConfiguration.Load(tempFile);
            Assert.Equal(0, config.RetryCount);
            Assert.Equal(500, config.RetryDelayMilliseconds); // 既定値 500ms
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// 定義されていないステージを GetPluginOrder に渡すと空リストが返ることを確認します。
    /// </summary>
    [Fact]
    public void GetPluginOrder_UndefinedStage_ReturnsEmptyList()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var json = """
        {
          "PluginsPath": "plugins",
          "StageOrders": [
            {
              "Stage": "Processing",
              "PluginOrder": [ { "Id": "plugin-a", "Order": 1 } ]
            }
          ]
        }
        """;
        File.WriteAllText(tempFile, json);

        try
        {
            var config = PluginConfiguration.Load(tempFile);
            var order = config.GetPluginOrder(new PluginStage("NonExistent"));
            Assert.Empty(order);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// GetPluginOrder はステージIDの大文字小文字を区別しないことを確認します。
    /// </summary>
    [Fact]
    public void GetPluginOrder_CaseInsensitiveStageId_ReturnsOrder()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var json = """
        {
          "PluginsPath": "plugins",
          "StageOrders": [
            {
              "Stage": "Processing",
              "PluginOrder": [ { "Id": "plugin-a", "Order": 1 } ]
            }
          ]
        }
        """;
        File.WriteAllText(tempFile, json);

        try
        {
            var config = PluginConfiguration.Load(tempFile);

            // 大文字小文字を変えても同一ステージとして取得できる
            var order = config.GetPluginOrder(new PluginStage("processing"));
            Assert.Single(order);
            Assert.Equal("plugin-a", order[0].Id);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// 不正な JSON の場合に例外が発生することを確認します。
    /// </summary>
    [Fact]
    public void Load_InvalidJson_ThrowsInvalidOperationException()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "{ INVALID JSON }");

        try
        {
            Assert.ThrowsAny<Exception>(() => PluginConfiguration.Load(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// IntervalMilliseconds が負値の場合に例外が発生することを確認します。
    /// </summary>
    [Fact]
    public void Load_NegativeIntervalMilliseconds_ThrowsInvalidOperationException()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
        {
          "PluginsPath": "plugins",
          "IntervalMilliseconds": -1
        }
        """);

        try
        {
            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => PluginConfiguration.Load(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// TimeoutMilliseconds が負値の場合に例外が発生することを確認します。
    /// </summary>
    [Fact]
    public void Load_NegativeTimeoutMilliseconds_ThrowsInvalidOperationException()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
        {
          "PluginsPath": "plugins",
          "TimeoutMilliseconds": -1
        }
        """);

        try
        {
            Assert.Throws<InvalidOperationException>(() => PluginConfiguration.Load(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// RetryCount が負値の場合に例外が発生することを確認します。
    /// </summary>
    [Fact]
    public void Load_NegativeRetryCount_ThrowsInvalidOperationException()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
        {
          "PluginsPath": "plugins",
          "RetryCount": -1
        }
        """);

        try
        {
            Assert.Throws<InvalidOperationException>(() => PluginConfiguration.Load(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// RetryDelayMilliseconds が負値の場合に例外が発生することを確認します。
    /// </summary>
    [Fact]
    public void Load_NegativeRetryDelayMilliseconds_ThrowsInvalidOperationException()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
        {
          "PluginsPath": "plugins",
          "RetryDelayMilliseconds": -1
        }
        """);

        try
        {
            Assert.Throws<InvalidOperationException>(() => PluginConfiguration.Load(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// PluginOrder の Order が負値の場合に例外が発生することを確認します。
    /// </summary>
    [Fact]
    public void Load_NegativePluginOrder_ThrowsInvalidOperationException()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
        {
          "PluginsPath": "plugins",
          "StageOrders": [
            {
              "Stage": "Processing",
              "PluginOrder": [ { "Id": "plugin-a", "Order": -1 } ]
            }
          ]
        }
        """);

        try
        {
            Assert.Throws<InvalidOperationException>(() => PluginConfiguration.Load(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// 設定ファイル内でプラグイン ID が重複している場合に例外がスローされることを確認します。
    /// </summary>
    [Fact]
    public void Load_DuplicatePluginId_ThrowsInvalidOperationException()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var json = """
        {
          "PluginsPath": "test-plugins",
          "StageOrders": [
            {
              "Stage": "Processing",
              "PluginOrder": [
                { "Id": "plugin-a", "Order": 1 },
                { "Id": "plugin-b", "Order": 2 },
                { "Id": "plugin-a", "Order": 3 }
              ]
            }
          ]
        }
        """;
        File.WriteAllText(tempFile, json);

        try
        {
            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => PluginConfiguration.Load(tempFile));
            Assert.Contains("plugin-a", ex.Message);
            Assert.Contains("重複", ex.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// 異なるステージ間で同じプラグイン ID を使用できることを確認します。
    /// </summary>
    [Fact]
    public void Load_SameIdInDifferentStages_Success()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var json = """
        {
          "PluginsPath": "test-plugins",
          "StageOrders": [
            {
              "Stage": "PreProcessing",
              "PluginOrder": [
                { "Id": "common-plugin", "Order": 1 }
              ]
            },
            {
              "Stage": "Processing",
              "PluginOrder": [
                { "Id": "common-plugin", "Order": 1 }
              ]
            }
          ]
        }
        """;
        File.WriteAllText(tempFile, json);

        try
        {
            // Act
            var config = PluginConfiguration.Load(tempFile);

            // Assert
            Assert.NotNull(config);
            Assert.Equal(2, config.StageOrders.Count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// StageOrders の MaxDegreeOfParallelism が正しく読み込まれることを確認します。
    /// </summary>
    [Fact]
    public void Load_WithStageMaxDegreeOfParallelism_ReturnsConfiguration()
    {
        var tempFile = Path.GetTempFileName();
        var json = """
        {
          "PluginsPath": "plugins",
          "StageOrders": [
            {
              "Stage": "Processing",
              "MaxDegreeOfParallelism": 2,
              "PluginOrder": [
                { "Id": "plugin-a", "Order": 1 }
              ]
            }
          ]
        }
        """;
        File.WriteAllText(tempFile, json);

        try
        {
            var config = PluginConfiguration.Load(tempFile);
            Assert.Equal(2, config.GetStageMaxDegreeOfParallelism(PluginStage.Processing));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetStageMaxDegreeOfParallelism_UndefinedStage_ReturnsNull()
    {
        var config = new PluginConfiguration
        {
            PluginsPath = "plugins",
            StageOrders =
            [
                new PluginStageOrderEntry
                {
                    Stage = PluginStage.Processing,
                    MaxDegreeOfParallelism = 2,
                }
            ]
        };

        var parallelism = config.GetStageMaxDegreeOfParallelism(PluginStage.PostProcessing);

        Assert.Null(parallelism);
    }

    [Fact]
    public void GetStageMaxDegreeOfParallelism_SecondCall_UsesCache()
    {
        var config = new PluginConfiguration
        {
            PluginsPath = "plugins",
            StageOrders =
            [
                new PluginStageOrderEntry
                {
                    Stage = PluginStage.Processing,
                    MaxDegreeOfParallelism = 2,
                }
            ]
        };

        var first = config.GetStageMaxDegreeOfParallelism(PluginStage.Processing);
        var second = config.GetStageMaxDegreeOfParallelism(PluginStage.Processing);

        Assert.Equal(2, first);
        Assert.Equal(2, second);
    }

    /// <summary>
    /// StageOrders の MaxDegreeOfParallelism が 0 以下の場合に例外が発生することを確認します。
    /// </summary>
    [Fact]
    public void Load_InvalidStageMaxDegreeOfParallelism_ThrowsInvalidOperationException()
    {
        var tempFile = Path.GetTempFileName();
        var json = """
        {
          "PluginsPath": "plugins",
          "StageOrders": [
            {
              "Stage": "Processing",
              "MaxDegreeOfParallelism": 0,
              "PluginOrder": [
                { "Id": "plugin-a", "Order": 1 }
              ]
            }
          ]
        }
        """;
        File.WriteAllText(tempFile, json);

        try
        {
            Assert.Throws<InvalidOperationException>(() => PluginConfiguration.Load(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
