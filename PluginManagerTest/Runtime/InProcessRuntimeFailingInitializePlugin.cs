using System.Collections.Frozen;
using PluginManager;

namespace PluginManagerTest;

/// <summary>
/// <see cref="InProcessPluginRuntimeTests"/> で使用する初期化失敗プラグインです。
/// </summary>
public sealed class InProcessRuntimeFailingInitializePlugin : IPlugin
{
    public string Id => nameof(InProcessRuntimeFailingInitializePlugin);
    public string Name => nameof(InProcessRuntimeFailingInitializePlugin);
    public Version Version => new(1, 0, 0);
    public IReadOnlySet<PluginStage> SupportedStages { get; } = new[] { PluginStage.Processing }.ToFrozenSet();

    public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("初期化失敗");

    public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
        => Task.FromResult<object?>(null);
}
