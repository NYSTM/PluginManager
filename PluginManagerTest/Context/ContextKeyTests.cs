using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// ContextKey の型安全性テストです。
/// </summary>
public sealed class ContextKeyTests
{
    // テスト用のキー定義
    private static readonly ContextKey<int> CountKey = new("Count");
    private static readonly ContextKey<string> NameKey = new("Name");
    private static readonly ContextKey<DateTime> TimestampKey = new("Timestamp");

    [Fact]
    public void SetProperty_WithContextKey_StoresValueCorrectly()
    {
        var context = new PluginContext();
        
        context.SetProperty(CountKey, 42);
        
        Assert.Equal(42, context.GetProperty(CountKey));
    }

    [Fact]
    public void GetProperty_WithContextKey_ReturnsCorrectType()
    {
        var context = new PluginContext();
        context.SetProperty(NameKey, "Test");

        var name = context.GetProperty(NameKey);

        Assert.Equal("Test", name);
        // コンパイル時に型チェック: name は string 型
    }

    [Fact]
    public void GetProperty_WithNonExistentKey_ReturnsDefault()
    {
        var context = new PluginContext();

        var count = context.GetProperty(CountKey);

        Assert.Equal(0, count);  // int のデフォルト値
    }

    [Fact]
    public void TryGetProperty_WithContextKey_ReturnsTrue()
    {
        var context = new PluginContext();
        context.SetProperty(CountKey, 100);

        var success = context.TryGetProperty(CountKey, out var count);

        Assert.True(success);
        Assert.Equal(100, count);
    }

    [Fact]
    public void TryGetProperty_WithNonExistentKey_ReturnsFalse()
    {
        var context = new PluginContext();

        var success = context.TryGetProperty(CountKey, out var count);

        Assert.False(success);
        Assert.Equal(0, count);
    }

    [Fact]
    public void GetPropertyOrDefault_WithContextKey_ReturnsDefaultValue()
    {
        var context = new PluginContext();

        var count = context.GetPropertyOrDefault(CountKey, 999);

        Assert.Equal(999, count);
    }

    [Fact]
    public void GetPropertyOrThrow_WithContextKey_ThrowsWhenMissing()
    {
        var context = new PluginContext();

        var ex = Assert.Throws<KeyNotFoundException>(() => context.GetPropertyOrThrow(CountKey));

        Assert.Contains("Count", ex.Message);
    }

    [Fact]
    public void RemoveProperty_WithContextKey_RemovesValue()
    {
        var context = new PluginContext();
        context.SetProperty(CountKey, 42);

        var removed = context.RemoveProperty(CountKey);

        Assert.True(removed);
        Assert.False(context.TryGetProperty(CountKey, out _));
    }

    [Fact]
    public void ContextKey_PreventsTypeMismatch()
    {
        var context = new PluginContext();
        context.SetProperty(CountKey, 42);

        // コンパイルエラー: 型が不一致
        // context.SetProperty(CountKey, "文字列");  // ← これはコンパイルできない

        // 実行時エラーを回避（文字列キーでは可能だった）
        var count = context.GetProperty(CountKey);
        Assert.Equal(42, count);
    }

    [Fact]
    public void MultipleContextKeys_WithSameName_AreDifferent()
    {
        var key1 = new ContextKey<int>("SameKey");
        var key2 = new ContextKey<string>("SameKey");

        var context = new PluginContext();
        context.SetProperty(key1, 42);

        // 同じ名前だが、型が異なる場合は TryGetProperty が false を返す
        var success = context.TryGetProperty(key2, out var stringValue);

        Assert.False(success);
        Assert.Null(stringValue);
    }

    [Fact]
    public void ContextKey_ToString_ReturnsName()
    {
        var key = new ContextKey<int>("MyKey");

        Assert.Equal("MyKey", key.ToString());
    }

    [Fact]
    public void ContextKey_ImplicitConversion_WorksCorrectly()
    {
        var context = new PluginContext();
        context.SetProperty(CountKey, 42);

        // 暗黙的な変換を利用
        string keyName = CountKey;
        Assert.Equal("Count", keyName);

        // 文字列キーでも取得可能（互換性）
        var count = context.GetProperty<int>("Count");
        Assert.Equal(42, count);
    }

    [Fact]
    public void ContextKey_NullName_ThrowsException()
    {
        Assert.Throws<ArgumentNullException>(() => new ContextKey<int>(null!));
    }

    [Fact]
    public void ContextKey_WithComplexType_WorksCorrectly()
    {
        var key = new ContextKey<List<string>>("Items");
        var context = new PluginContext();
        var items = new List<string> { "A", "B", "C" };

        context.SetProperty(key, items);
        var retrieved = context.GetProperty(key);

        Assert.NotNull(retrieved);
        Assert.Equal(3, retrieved!.Count);
        Assert.Same(items, retrieved);  // 参照が一致
    }
}
