using System.Reflection;
using System.Text.Json;
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
    /// RemoveProperty で大文字小文字を区別しないことを確認します。
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

    // ──────────────────────────────────────────────
    // TryGetProperty のテスト
    // ──────────────────────────────────────────────

    /// <summary>
    /// TryGetProperty で存在するキーの値を取得できることを確認します。
    /// </summary>
    [Fact]
    public void TryGetProperty_ExistingKey_ReturnsTrueAndValue()
    {
        // Arrange
        var context = new PluginContext();
        context.SetProperty("count", 42);

        // Act
        var result = context.TryGetProperty<int>("count", out var value);

        // Assert
        Assert.True(result);
        Assert.Equal(42, value);
    }

    /// <summary>
    /// TryGetProperty で存在しないキーは false を返すことを確認します。
    /// </summary>
    [Fact]
    public void TryGetProperty_NonExistentKey_ReturnsFalse()
    {
        // Arrange
        var context = new PluginContext();

        // Act
        var result = context.TryGetProperty<int>("count", out var value);

        // Assert
        Assert.False(result);
        Assert.Equal(0, value);  // default(int)
    }

    /// <summary>
    /// TryGetProperty で型が不一致の場合は false を返すことを確認します。
    /// </summary>
    [Fact]
    public void TryGetProperty_TypeMismatch_ReturnsFalse()
    {
        // Arrange
        var context = new PluginContext();
        context.SetProperty("value", "abc");  // string を格納

        // Act
        var result = context.TryGetProperty<int>("value", out var value);  // int として取得

        // Assert
        Assert.False(result);
        Assert.Equal(0, value);  // default(int)
    }

    /// <summary>
    /// TryGetProperty で null 値を取得できることを確認します。
    /// </summary>
    [Fact]
    public void TryGetProperty_NullValue_ReturnsTrueAndNull()
    {
        // Arrange
        var context = new PluginContext();
        context.SetProperty("data", null);

        // Act
        var result = context.TryGetProperty<string>("data", out var value);

        // Assert
        Assert.True(result);  // キーは存在する
        Assert.Null(value);
    }

    /// <summary>
    /// TryGetProperty で null の Nullable 値型を取得できることを確認します。
    /// </summary>
    [Fact]
    public void TryGetProperty_NullNullableValueType_ReturnsTrue()
    {
        var context = new PluginContext();
        context.SetProperty("nullable-int", null);

        var result = context.TryGetProperty<int?>("nullable-int", out var value);

        Assert.True(result);
        Assert.Null(value);
    }

    /// <summary>
    /// TryGetProperty で null 値を値型として取得した場合の失敗経路を確認します。
    /// </summary>
    [Fact]
    public void TryGetProperty_NullValueType_ReturnsFalse()
    {
        var context = new PluginContext();
        context.SetProperty("count", null);

        var result = context.TryGetProperty<int>("count", out var value);

        Assert.False(result);
        Assert.Equal(0, value);
    }

    // ──────────────────────────────────────────────
    // GetPropertyOrDefault のテスト
    // ──────────────────────────────────────────────

    /// <summary>
    /// GetPropertyOrDefault で存在するキーの値を取得できることを確認します。
    /// </summary>
    [Fact]
    public void GetPropertyOrDefault_ExistingKey_ReturnsValue()
    {
        // Arrange
        var context = new PluginContext();
        context.SetProperty("count", 42);

        // Act
        var value = context.GetPropertyOrDefault("count", 100);

        // Assert
        Assert.Equal(42, value);
    }

    /// <summary>
    /// GetPropertyOrDefault で存在しないキーはデフォルト値を返すことを確認します。
    /// </summary>
    [Fact]
    public void GetPropertyOrDefault_NonExistentKey_ReturnsDefault()
    {
        // Arrange
        var context = new PluginContext();

        // Act
        var value = context.GetPropertyOrDefault("count", 100);

        // Assert
        Assert.Equal(100, value);
    }

    /// <summary>
    /// GetPropertyOrDefault で型が不一致の場合はデフォルト値を返すことを確認します。
    /// </summary>
    [Fact]
    public void GetPropertyOrDefault_TypeMismatch_ReturnsDefault()
    {
        // Arrange
        var context = new PluginContext();
        context.SetProperty("value", "abc");

        // Act
        var value = context.GetPropertyOrDefault("value", 999);

        // Assert
        Assert.Equal(999, value);
    }

    // ──────────────────────────────────────────────
    // GetPropertyOrThrow のテスト
    // ──────────────────────────────────────────────

    /// <summary>
    /// GetPropertyOrThrow で存在するキーの値を取得できることを確認します。
    /// </summary>
    [Fact]
    public void GetPropertyOrThrow_ExistingKey_ReturnsValue()
    {
        // Arrange
        var context = new PluginContext();
        context.SetProperty("count", 42);

        // Act
        var value = context.GetPropertyOrThrow<int>("count");

        // Assert
        Assert.Equal(42, value);
    }

    /// <summary>
    /// GetPropertyOrThrow で存在しないキーは KeyNotFoundException をスローすることを確認します。
    /// </summary>
    [Fact]
    public void GetPropertyOrThrow_NonExistentKey_ThrowsKeyNotFoundException()
    {
        // Arrange
        var context = new PluginContext();

        // Act & Assert
        var ex = Assert.Throws<KeyNotFoundException>(() => context.GetPropertyOrThrow<int>("count"));
        Assert.Contains("count", ex.Message);
    }

    /// <summary>
    /// GetPropertyOrThrow で型が不一致の場合は InvalidCastException をスローすることを確認します。
    /// </summary>
    [Fact]
    public void GetPropertyOrThrow_TypeMismatch_ThrowsInvalidCastException()
    {
        // Arrange
        var context = new PluginContext();
        context.SetProperty("value", "abc");

        // Act & Assert
        var ex = Assert.Throws<InvalidCastException>(() => context.GetPropertyOrThrow<int>("value"));
        Assert.Contains("value", ex.Message);
        Assert.Contains("String", ex.Message);
        Assert.Contains("Int32", ex.Message);
    }

    /// <summary>
    /// GetPropertyOrThrow で null 値を取得できることを確認します。
    /// </summary>
    [Fact]
    public void GetPropertyOrThrow_NullValue_ReturnsNull()
    {
        // Arrange
        var context = new PluginContext();
        context.SetProperty("data", null);

        // Act
        var value = context.GetPropertyOrThrow<string>("data");

        // Assert
        Assert.Null(value);
    }

    /// <summary>
    /// コンテキストキーを使用してプロパティを設定し、取得できることを確認します。
    /// </summary>
    [Fact]
    public void SetProperty_WithContextKey_CanRetrieveByContextKey()
    {
        var context = new PluginContext();
        var key = new ContextKey<int>("RequestCount");

        context.SetProperty(key, 42);

        Assert.Equal(42, context.GetProperty(key));
    }

    /// <summary>
    /// コンテキストキーを使用した TryGetProperty で値を取得できることを確認します。
    /// </summary>
    [Fact]
    public void TryGetProperty_WithContextKey_ReturnsTrueAndValue()
    {
        var context = new PluginContext();
        var key = new ContextKey<string>("UserName");
        context.SetProperty(key, "alice");

        var result = context.TryGetProperty(key, out string? value);

        Assert.True(result);
        Assert.Equal("alice", value);
    }

    /// <summary>
    /// コンテキストキーを使用した GetPropertyOrDefault でデフォルト値が返ることを確認します。
    /// </summary>
    [Fact]
    public void GetPropertyOrDefault_WithContextKey_ReturnsDefaultWhenMissing()
    {
        var context = new PluginContext();
        var key = new ContextKey<int>("RetryCount");

        var value = context.GetPropertyOrDefault(key, 5);

        Assert.Equal(5, value);
    }

    /// <summary>
    /// コンテキストキーを使用した GetPropertyOrThrow で値を取得できることを確認します。
    /// </summary>
    [Fact]
    public void GetPropertyOrThrow_WithContextKey_ReturnsValue()
    {
        var context = new PluginContext();
        var key = new ContextKey<string>("Tenant");
        context.SetProperty(key, "A");

        var value = context.GetPropertyOrThrow(key);

        Assert.Equal("A", value);
    }

    /// <summary>
    /// コンテキストキーを使用した RemoveProperty でエントリが削除できることを確認します。
    /// </summary>
    [Fact]
    public void RemoveProperty_WithContextKey_RemovesEntry()
    {
        var context = new PluginContext();
        var key = new ContextKey<string>("Tenant");
        context.SetProperty(key, "A");

        var result = context.RemoveProperty(key);

        Assert.True(result);
        Assert.False(context.Properties.ContainsKey("Tenant"));
    }

    /// <summary>
    /// すべてのプロパティを取得できることを確認します。
    /// </summary>
    [Fact]
    public void GetAllProperties_ReturnsAllEntries()
    {
        var context = new PluginContext();
        context.SetProperty("Key1", "Value1");
        context.SetProperty("Key2", 42);

        var properties = context.GetAllProperties().ToDictionary(x => x.Key, x => x.Value);

        Assert.Equal(2, properties.Count);
        Assert.Equal("Value1", properties["Key1"]);
        Assert.Equal(42, properties["Key2"]);
    }

    [Fact]
    public void ApplyJsonDictionary_WithArrayAndObject_PreservesJsonElement()
    {
        var context = new PluginContext();
        var data = new Dictionary<string, JsonElement>
        {
            ["array"] = JsonSerializer.SerializeToElement(new[] { 1, 2, 3 }),
            ["object"] = JsonSerializer.SerializeToElement(new { Name = "Plugin" }),
        };

        context.ApplyJsonDictionary(data);

        Assert.True(context.TryGetProperty<JsonElement>("array", out var array));
        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        Assert.Equal(3, array.GetArrayLength());

        Assert.True(context.TryGetProperty<JsonElement>("object", out var obj));
        Assert.Equal(JsonValueKind.Object, obj.ValueKind);
        Assert.Equal("Plugin", obj.GetProperty("Name").GetString());
    }

    [Fact]
    public void DeserializeJsonElement_Undefined_ReturnsNull()
    {
        var method = typeof(PluginContext).GetMethod("DeserializeJsonElement", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = method!.Invoke(null, [default(JsonElement)]);

        Assert.Null(result);
    }
}
