using System.Collections.Frozen;
using PluginManager;

namespace PluginManagerTest;

/// <summary>
/// `PluginResourceHealthTests` の out-of-process 監視で使用するプラグインです。
/// </summary>
public sealed class OutOfProcessMonitoringPlugin : IPlugin
{
    public string Id => "out-of-process-monitoring-plugin";
    public string Name => "OutOfProcessMonitoringPlugin";
    public Version Version => new(1, 0, 0);
    public IReadOnlySet<PluginStage> SupportedStages { get; } = new[] { PluginStage.Processing }.ToFrozenSet();
    public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default) => Task.FromResult<object?>("ok");
}
