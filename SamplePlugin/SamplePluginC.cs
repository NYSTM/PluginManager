using PluginManager;
using System.Collections.Frozen;

namespace SamplePlugin;

/// <summary>
/// サンプルプラグインC（Processingステージで実行）
/// </summary>
[Plugin("sample-plugin-c", "サンプルプラグインC", "1.0.0", "Processing")]
public sealed class SamplePluginC : IPlugin
{
    public string Id => "sample-plugin-c";
    public string Name => "サンプルプラグインC";
    public Version Version => new(1, 0, 0);
    public IReadOnlySet<PluginStage> SupportedStages { get; } =
        new[] { PluginStage.Processing }.ToFrozenSet();

    public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[{Name}] 初期化を開始します。");
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine($"[{Name}] 初期化が完了しました。");
        return Task.CompletedTask;
    }

    public async Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[{Name}] Processingステージ実行開始");
        await Task.Delay(400, cancellationToken);
        var result = $"{Name}: Processing完了 at {DateTime.Now:HH:mm:ss}";
        Console.WriteLine($"[{Name}] {result}");
        return result;
    }
}
