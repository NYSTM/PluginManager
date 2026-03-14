using System.Collections.Frozen;
using PluginManager;

namespace PluginManagerTest;

/// <summary>
/// <see cref="InProcessPluginRuntimeTests"/> で使用する成功プラグインです。
/// </summary>
public sealed class InProcessRuntimeSuccessfulPlugin : IPlugin
{
    public const string InitializedKey = "InProcessRuntimeSuccessfulPlugin.Initialized";
    public const string InitializedValue = "initialized";

    public string Id => nameof(InProcessRuntimeSuccessfulPlugin);
    public string Name => nameof(InProcessRuntimeSuccessfulPlugin);
    public Version Version => new(1, 0, 0);
    public IReadOnlySet<PluginStage> SupportedStages { get; } = new[] { PluginStage.Processing }.ToFrozenSet();

    public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
    {
        context.SetProperty(InitializedKey, InitializedValue);
        return Task.CompletedTask;
    }

    public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
        => Task.FromResult<object?>(InitializedValue);
}
