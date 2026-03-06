using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// PluginStage のユニットテスト
/// </summary>
public sealed class PluginStageTests
{
    [Fact]
    public void Constructor_EmptyId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new PluginStage(string.Empty));
        Assert.Throws<ArgumentException>(() => new PluginStage("   "));
        Assert.Throws<ArgumentException>(() => new PluginStage(null!));
    }

    [Fact]
    public void Constructor_ValidId_SetsId()
    {
        var stage = new PluginStage("MyStage");
        Assert.Equal("MyStage", stage.Id);
    }

    [Fact]
    public void Equals_SameId_ReturnsTrue()
    {
        var a = new PluginStage("Processing");
        var b = new PluginStage("Processing");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equals_DifferentCase_ReturnsTrue()
    {
        // Id の比較は大文字小文字を区別しない
        var a = new PluginStage("processing");
        var b = new PluginStage("PROCESSING");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equals_DifferentId_ReturnsFalse()
    {
        var a = new PluginStage("PreProcessing");
        var b = new PluginStage("PostProcessing");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void EqualityOperator_SameId_ReturnsTrue()
    {
        var a = new PluginStage("Processing");
        var b = new PluginStage("Processing");
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void EqualityOperator_DifferentId_ReturnsFalse()
    {
        var a = new PluginStage("PreProcessing");
        var b = new PluginStage("PostProcessing");
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void EqualityOperator_NullComparison_ReturnsExpected()
    {
        PluginStage? a = null;
        PluginStage? b = null;
        Assert.True(a == b);
        Assert.False(new PluginStage("Processing") == null);
        Assert.False(null == new PluginStage("Processing"));
    }

    [Fact]
    public void GetHashCode_SameIdDifferentCase_ReturnsSameHash()
    {
        var a = new PluginStage("processing");
        var b = new PluginStage("PROCESSING");
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsId()
    {
        var stage = new PluginStage("Validation");
        Assert.Equal("Validation", stage.ToString());
    }

    [Fact]
    public void StaticConstants_HaveExpectedIds()
    {
        Assert.Equal("PreProcessing",  PluginStage.PreProcessing.Id);
        Assert.Equal("Processing",     PluginStage.Processing.Id);
        Assert.Equal("PostProcessing", PluginStage.PostProcessing.Id);
    }

    [Fact]
    public void Equals_WithObject_ReturnsExpected()
    {
        var stage = new PluginStage("Processing");
        Assert.True(stage.Equals((object)new PluginStage("processing")));
        Assert.False(stage.Equals("Processing")); // 異なる型
        Assert.False(stage.Equals(null));
    }

    [Fact]
    public void UsedAsHashSetKey_CaseInsensitiveLookup()
    {
        // HashSet での大文字小文字を区別しない動作
        var set = new HashSet<PluginStage> { new("Processing") };
        Assert.Contains(new PluginStage("processing"), set);
    }
}
