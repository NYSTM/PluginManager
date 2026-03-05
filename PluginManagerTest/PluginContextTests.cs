using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// PluginContext のユニットテスト
/// </summary>
public sealed class PluginContextTests
{
    /// <summary>
    /// 既定コンストラクターで空のプロパティ辞書が初期化されることを確認します。
    /// </summary>
    [Fact]
    public void Constructor_Default_InitializesEmptyProperties()
    {
        // Act
        var context = new PluginContext();

        // Assert
        Assert.NotNull(context.Properties);
        Assert.Empty(context.Properties);
    }

    /// <summary>
    /// 値を設定したキーから同じ値を取得できることを確認します。
    /// </summary>
    [Fact]
    public void SetProperty_WithValue_CanRetrieve()
    {
        // Arrange
        var context = new PluginContext();

        // Act
        context.SetProperty("TestKey", "TestValue");
        var value = context.GetProperty<string>("TestKey");

        // Assert
        Assert.Equal("TestValue", value);
    }

    /// <summary>
    /// 存在しないキー取得時に既定値が返ることを確認します。
    /// </summary>
    [Fact]
    public void GetProperty_NonExistentKey_ReturnsDefault()
    {
        // Arrange
        var context = new PluginContext();

        // Act
        var value = context.GetProperty<string>("NonExistentKey");

        // Assert
        Assert.Null(value);
    }

    /// <summary>
    /// 型不一致の値取得時に既定値が返ることを確認します。
    /// </summary>
    [Fact]
    public void GetProperty_TypeMismatch_ReturnsDefault()
    {
        // Arrange
        var context = new PluginContext();
        context.SetProperty("TestKey", 123);

        // Act
        var value = context.GetProperty<string>("TestKey");

        // Assert
        Assert.Null(value);
    }

    /// <summary>
    /// 複数スレッドからの設定操作が安全に行えることを確認します。
    /// </summary>
    [Fact]
    public async Task SetProperty_MultipleThreads_IsThreadSafe()
    {
        // Arrange
        var context = new PluginContext();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                context.SetProperty($"Key{index}", $"Value{index}");
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        for (int i = 0; i < 100; i++)
        {
            var value = context.GetProperty<string>($"Key{i}");
            Assert.Equal($"Value{i}", value);
        }
    }

    /// <summary>
    /// キーの大文字小文字を区別せずに上書きできることを確認します。
    /// </summary>
    [Fact]
    public void SetProperty_CaseInsensitiveKey_OverwritesValue()
    {
        // Arrange
        var context = new PluginContext();

        // Act
        context.SetProperty("TestKey", "Value1");
        context.SetProperty("testkey", "Value2");
        var value = context.GetProperty<string>("TESTKEY");

        // Assert
        Assert.Equal("Value2", value);
    }

    /// <summary>
    /// 初期プロパティ指定コンストラクターで値が設定されることを確認します。
    /// </summary>
    [Fact]
    public void Constructor_WithInitialProperties_SetsProperties()
    {
        // Arrange
        var initialProperties = new Dictionary<string, object?>
        {
            ["Key1"] = "Value1",
            ["Key2"] = 123
        };

        // Act
        var context = new PluginContext(properties: initialProperties);

        // Assert
        Assert.Equal("Value1", context.GetProperty<string>("Key1"));
        Assert.Equal(123, context.GetProperty<int>("Key2"));
    }

    /// <summary>
    /// CreateScope はプロパティのスナップショットを持つ独立したコンテキストを返すことを確認します。
    /// </summary>
    [Fact]
    public void CreateScope_CopiesProperties_IndependentFromOriginal()
    {
        // Arrange
        var original = new PluginContext();
        original.SetProperty("Key1", "Value1");

        // Act
        var scope = original.CreateScope();

        // スコープ生成後に元を変更してもスコープに影響しない
        original.SetProperty("Key1", "Modified");
        original.SetProperty("Key2", "OnlyInOriginal");

        // Assert
        Assert.Equal("Value1", scope.GetProperty<string>("Key1"));   // スナップショット時の値
        Assert.Null(scope.GetProperty<string>("Key2"));               // 生成後の追加は反映されない
    }

    /// <summary>
    /// CreateScope で生成したスコープへの書き込みが元のコンテキストに影響しないことを確認します。
    /// </summary>
    [Fact]
    public void CreateScope_WriteToScope_DoesNotAffectOriginal()
    {
        // Arrange
        var original = new PluginContext();
        original.SetProperty("Key1", "Original");
        var scope = original.CreateScope();

        // Act
        scope.SetProperty("Key1", "ScopeModified");
        scope.SetProperty("NewKey", "NewValue");

        // Assert
        Assert.Equal("Original", original.GetProperty<string>("Key1")); // 元は変わらない
        Assert.Null(original.GetProperty<string>("NewKey"));
    }

    /// <summary>
    /// null 値を SetProperty で保存し GetProperty で取得できることを確認します。
    /// </summary>
    [Fact]
    public void SetProperty_NullValue_CanRetrieve()
    {
        // Arrange
        var context = new PluginContext();

        // Act
        context.SetProperty("NullKey", null);

        // null を明示的にセットした場合は null が返る（キー不在とは異なる）
        Assert.True(context.Properties.ContainsKey("NullKey"));
        Assert.Null(context.GetProperty<string>("NullKey"));
    }

    /// <summary>
    /// SetProperty で既存キーを上書きすると新しい値が返ることを確認します。
    /// </summary>
    [Fact]
    public void SetProperty_OverwriteExistingKey_ReturnsNewValue()
    {
        // Arrange
        var context = new PluginContext();
        context.SetProperty("Key", "First");

        // Act
        context.SetProperty("Key", "Second");

        // Assert
        Assert.Equal("Second", context.GetProperty<string>("Key"));
    }

    /// <summary>
    /// CreateScope は空のコンテキストからコピーできることを確認します。
    /// </summary>
    [Fact]
    public void CreateScope_FromEmptyContext_ReturnsEmptyScope()
    {
        var context = new PluginContext();
        var scope = context.CreateScope();
        Assert.Empty(scope.Properties);
    }

    /// <summary>
    /// RemoveProperty で存在するキーを削除できることを確認します。
    /// </summary>
    [Fact]
    public void RemoveProperty_ExistingKey_ReturnsTrueAndRemovesKey()
    {
        // Arrange
        var context = new PluginContext();
        context.SetProperty("Key1", "Value1");

        // Act
        var result = context.RemoveProperty("Key1");

        // Assert
        Assert.True(result);
        Assert.Null(context.GetProperty<string>("Key1"));
        Assert.False(context.Properties.ContainsKey("Key1"));
    }

    /// <summary>
    /// RemoveProperty で存在しないキーを削除しようとすると false を返すことを確認します。
    /// </summary>
    [Fact]
    public void RemoveProperty_NonExistentKey_ReturnsFalse()
    {
        // Arrange
        var context = new PluginContext();

        // Act
        var result = context.RemoveProperty("NonExistentKey");

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Clear ですべてのプロパティが削除されることを確認します。
    /// </summary>
    [Fact]
    public void Clear_RemovesAllProperties()
    {
        // Arrange
        var context = new PluginContext();
        context.SetProperty("Key1", "Value1");
        context.SetProperty("Key2", 42);
        context.SetProperty("Key3", true);

        // Act
        context.Clear();

        // Assert
        Assert.Empty(context.Properties);
    }

    /// <summary>
    /// Clear 後に新しいプロパティを追加できることを確認します。
    /// </summary>
    [Fact]
    public void Clear_ThenSetProperty_WorksCorrectly()
    {
        // Arrange
        var context = new PluginContext();
        context.SetProperty("OldKey", "OldValue");
        context.Clear();

        // Act
        context.SetProperty("NewKey", "NewValue");

        // Assert
        Assert.Single(context.Properties);
        Assert.Equal("NewValue", context.GetProperty<string>("NewKey"));
        Assert.Null(context.GetProperty<string>("OldKey"));
    }

    /// <summary>
    /// RemoveProperty はキー大文字小文字を区別しないことを確認します。
    /// </summary>
    [Fact]
    public void RemoveProperty_CaseInsensitiveKey_RemovesEntry()
    {
        // Arrange
        var context = new PluginContext();
        context.SetProperty("MyKey", "Value");

        // Act
        var result = context.RemoveProperty("mykey");

        // Assert
        Assert.True(result);
        Assert.False(context.Properties.ContainsKey("MyKey"));
    }
}
