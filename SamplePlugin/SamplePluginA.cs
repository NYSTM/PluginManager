using PluginManager;
using System.Collections.Frozen;

namespace SamplePlugin;

/// <summary>
/// サンプルプラグインA（PreProcessingステージで実行）
/// </summary>
[Plugin("sample-plugin-a", "サンプルプラグインA", "1.0.0", "PreProcessing")]
public sealed class SamplePluginA : IPlugin
{
    public string Id => "sample-plugin-a";
    public string Name => "サンプルプラグインA";
    public Version Version => new(1, 0, 0);
    public IReadOnlySet<PluginStage> SupportedStages { get; } =
        new[] { PluginStage.PreProcessing }.ToFrozenSet();

    public async Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[{Name}] 初期化を開始します。");
        cancellationToken.ThrowIfCancellationRequested();
        context.SetProperty("InitializedBy", Name);
        context.SetProperty("InitializedAt", DateTime.Now);
        await Task.Delay(100, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine($"[{Name}] 初期化が完了しました。");
    }

    public async Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[{Name}] PreProcessingステージ実行開始");
        await Task.Delay(500, cancellationToken);
        var result = $"{Name}: PreProcessing完了 at {DateTime.Now:HH:mm:ss}";
        Console.WriteLine($"[{Name}] {result}");
        return result;
    }
}
