using PluginManager;
using System.Collections.Frozen;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// 責務分割されたインターフェースのテストです。
/// </summary>
public sealed class PluginInterfaceTests
{
    [Fact]
    public void IPluginMetadata_CanBeImplementedSeparately()
    {
        var plugin = new MetadataOnlyPlugin();

        Assert.Equal("metadata-only", plugin.Id);
        Assert.Equal("メタデータのみ", plugin.Name);
        Assert.Equal(new Version(1, 0, 0), plugin.Version);
        Assert.Contains(PluginStage.Processing, (IReadOnlySet<PluginStage>)plugin.SupportedStages);
    }

    [Fact]
    public async Task IPluginInitializer_CanBeImplementedSeparately()
    {
        var plugin = new InitializerOnlyPlugin();
        var context = new PluginContext();

        await plugin.InitializeAsync(context);

        Assert.True(context.TryGetProperty<bool>("Initialized", out var initialized));
        Assert.True(initialized);
    }

    [Fact]
    public async Task IStageExecutor_CanBeImplementedSeparately()
    {
        var executor = new ExecutorOnlyPlugin();
        var context = new PluginContext();

        var result = await executor.ExecuteAsync(PluginStage.Processing, context);

        Assert.Equal("実行完了", result);
    }

    [Fact]
    public void IPlugin_ComposesAllInterfaces()
    {
        var plugin = new FullPlugin();

        // IPluginMetadata
        Assert.NotNull(plugin.Id);
        Assert.NotNull(plugin.Name);
        Assert.NotNull(plugin.Version);
        Assert.NotNull(plugin.SupportedStages);

        // IPluginInitializer
        Assert.NotNull(plugin as IPluginInitializer);

        // IStageExecutor
        Assert.NotNull(plugin as IStageExecutor);
    }

    [Fact]
    public void PluginBase_InitializesMetadataCorrectly()
    {
        var plugin = new SimplePluginBase();

        Assert.Equal("simple", plugin.Id);
        Assert.Equal("シンプル", plugin.Name);
        Assert.Equal(new Version(1, 0, 0), plugin.Version);
        Assert.Contains(PluginStage.Processing, (IReadOnlySet<PluginStage>)plugin.SupportedStages);
    }

    [Fact]
    public async Task PluginBase_DefaultInitializeAsync_DoesNothing()
    {
        var plugin = new SimplePluginBase();
        var context = new PluginContext();

        // デフォルト実装は何もしない
        await plugin.InitializeAsync(context);

        // エラーが発生しないことを確認
        Assert.True(true);
    }

    [Fact]
    public async Task PluginBase_OnExecuteAsync_IsCalled()
    {
        var plugin = new SimplePluginBase();
        var context = new PluginContext();

        var result = await plugin.ExecuteAsync(PluginStage.Processing, context);

        Assert.Equal("実行完了", result);
    }

    [Fact]
    public async Task PluginBase_WithCustomInitialize_CallsOnInitializeAsync()
    {
        var plugin = new CustomInitPluginBase();
        var context = new PluginContext();

        await plugin.InitializeAsync(context);

        Assert.True(context.TryGetProperty<bool>("CustomInitialized", out var initialized));
        Assert.True(initialized);
    }

    [Fact]
    public async Task PluginBase_WithAsyncDisposable_DisposesCorrectly()
    {
        var plugin = new DisposablePluginBase();
        var context = new PluginContext();

        await plugin.InitializeAsync(context);
        await plugin.DisposeAsync();

        Assert.True(plugin.IsDisposed);
    }

    [Fact]
    public async Task PluginBase_AfterDispose_ThrowsObjectDisposedException()
    {
        var plugin = new SimplePluginBase();
        var context = new PluginContext();

        await plugin.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await plugin.ExecuteAsync(PluginStage.Processing, context));
    }

    [Fact]
    public void PluginBase_WithoutStages_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new NoStagePluginBase());
    }

    [Fact]
    public void PluginBase_WithEmptyId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new EmptyIdPluginBase());
    }

    // テスト用プラグイン実装

    private sealed class MetadataOnlyPlugin : IPluginMetadata
    {
        public string Id => "metadata-only";
        public string Name => "メタデータのみ";
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } =
            new[] { PluginStage.Processing }.ToFrozenSet();
    }

    private sealed class InitializerOnlyPlugin : IPluginInitializer
    {
        public async Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
        {
            context.SetProperty("Initialized", true);
            await Task.CompletedTask;
        }
    }

    private sealed class ExecutorOnlyPlugin : IStageExecutor
    {
        public async Task<object?> ExecuteAsync(
            PluginStage stage,
            PluginContext context,
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            return "実行完了";
        }
    }

    private sealed class FullPlugin : IPlugin
    {
        public string Id => "full";
        public string Name => "フル";
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } =
            new[] { PluginStage.Processing }.ToFrozenSet();

        public async Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
        }

        public async Task<object?> ExecuteAsync(
            PluginStage stage,
            PluginContext context,
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            return "完了";
        }
    }

    private sealed class SimplePluginBase : PluginBase
    {
        public SimplePluginBase()
            : base("simple", "シンプル", new Version(1, 0, 0), PluginStage.Processing)
        {
        }

        protected override async Task<object?> OnExecuteAsync(
            PluginStage stage,
            PluginContext context,
            CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            return "実行完了";
        }
    }

    private sealed class CustomInitPluginBase : PluginBase
    {
        public CustomInitPluginBase()
            : base("custom-init", "カスタム初期化", new Version(1, 0, 0), PluginStage.Processing)
        {
        }

        protected override async Task OnInitializeAsync(
            PluginContext context,
            CancellationToken cancellationToken)
        {
            context.SetProperty("CustomInitialized", true);
            await Task.CompletedTask;
        }

        protected override async Task<object?> OnExecuteAsync(
            PluginStage stage,
            PluginContext context,
            CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            return "完了";
        }
    }

    private sealed class DisposablePluginBase : PluginBase
    {
        public bool IsDisposed { get; private set; }

        public DisposablePluginBase()
            : base("disposable", "リソース管理", new Version(1, 0, 0), PluginStage.Processing)
        {
        }

        protected override async Task<object?> OnExecuteAsync(
            PluginStage stage,
            PluginContext context,
            CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            return "完了";
        }

        protected override async ValueTask DisposeAsyncCore()
        {
            IsDisposed = true;
            await base.DisposeAsyncCore();
        }
    }

    private sealed class NoStagePluginBase : PluginBase
    {
        public NoStagePluginBase()
            : base("no-stage", "ステージなし", new Version(1, 0, 0))
        {
        }

        protected override Task<object?> OnExecuteAsync(
            PluginStage stage,
            PluginContext context,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<object?>(null);
        }
    }

    private sealed class EmptyIdPluginBase : PluginBase
    {
        public EmptyIdPluginBase()
            : base("", "空ID", new Version(1, 0, 0), PluginStage.Processing)
        {
        }

        protected override Task<object?> OnExecuteAsync(
            PluginStage stage,
            PluginContext context,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<object?>(null);
        }
    }
}
