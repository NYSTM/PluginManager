using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// PluginStageRegistry のテストです。
/// </summary>
public sealed class PluginStageRegistryTests
{
    [Fact]
    public void GetAll_ReturnsStandardStages()
    {
        var all = PluginStageRegistry.GetAll();

        Assert.Contains(PluginStage.PreProcessing, (IReadOnlySet<PluginStage>)all);
        Assert.Contains(PluginStage.Processing, (IReadOnlySet<PluginStage>)all);
        Assert.Contains(PluginStage.PostProcessing, (IReadOnlySet<PluginStage>)all);
    }

    [Fact]
    public void IsRegistered_WithStandardStage_ReturnsTrue()
    {
        Assert.True(PluginStageRegistry.IsRegistered("PreProcessing"));
        Assert.True(PluginStageRegistry.IsRegistered("Processing"));
        Assert.True(PluginStageRegistry.IsRegistered("PostProcessing"));
    }

    [Fact]
    public void IsRegistered_WithUnknownStage_ReturnsFalse()
    {
        Assert.False(PluginStageRegistry.IsRegistered("UnknownStage"));
    }

    [Fact]
    public void Register_WithValidId_ReturnsStage()
    {
        var customStage = PluginStageRegistry.Register("CustomStage-" + Guid.NewGuid(), "カスタムステージ");

        Assert.NotNull(customStage);
        Assert.True(PluginStageRegistry.IsRegistered(customStage.Id));
    }

    [Fact]
    public void Register_WithDuplicateId_ThrowsInvalidOperationException()
    {
        var id = "DuplicateStage-" + Guid.NewGuid();
        PluginStageRegistry.Register(id, "最初の登録");

        Assert.Throws<InvalidOperationException>(() =>
            PluginStageRegistry.Register(id, "重複登録"));
    }

    [Fact]
    public void RegisterOrGet_WithNewId_ReturnsNewStage()
    {
        var id = "NewStage-" + Guid.NewGuid();
        var stage = PluginStageRegistry.RegisterOrGet(id, "新しいステージ");

        Assert.NotNull(stage);
        Assert.True(PluginStageRegistry.IsRegistered(id));
    }

    [Fact]
    public void RegisterOrGet_WithExistingId_ReturnsSameStage()
    {
        var id = "ExistingStage-" + Guid.NewGuid();
        var stage1 = PluginStageRegistry.RegisterOrGet(id, "最初の登録");
        var stage2 = PluginStageRegistry.RegisterOrGet(id, "重複登録");

        Assert.Same(stage1, stage2);
    }

    [Fact]
    public void TryGet_WithRegisteredStage_ReturnsTrue()
    {
        var id = "GetStage-" + Guid.NewGuid();
        PluginStageRegistry.Register(id, "取得テスト");

        var found = PluginStageRegistry.TryGet(id, out var stage);

        Assert.True(found);
        Assert.NotNull(stage);
        Assert.Equal(id, stage.Id, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryGet_WithUnregisteredStage_ReturnsFalse()
    {
        var found = PluginStageRegistry.TryGet("UnknownStage-" + Guid.NewGuid(), out var stage);

        Assert.False(found);
        Assert.Null(stage);
    }

    [Fact]
    public void Get_WithRegisteredStage_ReturnsStage()
    {
        var id = "GetStage2-" + Guid.NewGuid();
        PluginStageRegistry.Register(id, "取得テスト2");

        var stage = PluginStageRegistry.Get(id);

        Assert.NotNull(stage);
        Assert.Equal(id, stage.Id, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Get_WithUnregisteredStage_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PluginStageRegistry.Get("UnknownStage-" + Guid.NewGuid()));
    }

    [Fact]
    public void GetDescription_WithStandardStage_ReturnsDescription()
    {
        var description = PluginStageRegistry.GetDescription("PreProcessing");

        Assert.NotEmpty(description);
        Assert.Contains("前処理", description);
    }

    [Fact]
    public void GetDescription_WithCustomStage_ReturnsDescription()
    {
        var id = "DescStage-" + Guid.NewGuid();
        var expectedDesc = "カスタムステージの説明";
        PluginStageRegistry.Register(id, expectedDesc);

        var description = PluginStageRegistry.GetDescription(id);

        Assert.Equal(expectedDesc, description);
    }

    [Fact]
    public void GetDescription_WithUnregisteredStage_ReturnsEmptyString()
    {
        var description = PluginStageRegistry.GetDescription("UnknownStage-" + Guid.NewGuid());

        Assert.Equal(string.Empty, description);
    }

    [Fact]
    public void GetAllInfo_ReturnsAllStagesWithDescription()
    {
        var id = "InfoStage-" + Guid.NewGuid();
        PluginStageRegistry.Register(id, "情報テスト");

        var allInfo = PluginStageRegistry.GetAllInfo();

        Assert.NotEmpty(allInfo);
        Assert.True(allInfo.ContainsKey("PreProcessing"));
        Assert.True(allInfo.ContainsKey("Processing"));
        Assert.True(allInfo.ContainsKey("PostProcessing"));
        Assert.True(allInfo.ContainsKey(id));
    }

    [Fact]
    public void Register_CaseInsensitive()
    {
        var id = "CaseTest-" + Guid.NewGuid();
        PluginStageRegistry.Register(id, "大文字小文字テスト");

        Assert.True(PluginStageRegistry.IsRegistered(id.ToUpper()));
        Assert.True(PluginStageRegistry.IsRegistered(id.ToLower()));
        Assert.True(PluginStageRegistry.TryGet(id.ToUpper(), out _));
        Assert.True(PluginStageRegistry.TryGet(id.ToLower(), out _));
    }
}
