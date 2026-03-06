using PluginManager;
using System.Collections.Frozen;

namespace SamplePlugin;

/// <summary>
/// サンプルプラグインB（PostProcessingステージで実行）
/// </summary>
[Plugin("sample-plugin-b", "サンプルプラグインB", "1.0.0", "PostProcessing")]
public sealed class SamplePluginB : IPlugin
{
    public string Id => "sample-plugin-b";
    public string Name => "サンプルプラグインB";
    public Version Version => new(1, 0, 0);
    public IReadOnlySet<PluginStage> SupportedStages { get; } =
        new[] { PluginStage.PostProcessing }.ToFrozenSet();

    public async Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[{Name}] 初期化を開始します。");
        cancellationToken.ThrowIfCancellationRequested();
        var initializedBy = context.GetProperty<string>("InitializedBy");
        if (!string.IsNullOrEmpty(initializedBy))
            Console.WriteLine($"[{Name}] 先に初期化されたプラグイン: {initializedBy}");
        context.SetProperty($"{Name}_Timestamp", DateTime.Now);
        await Task.Delay(150, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine($"[{Name}] 初期化が完了しました。");
    }

    public async Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[{Name}] PostProcessingステージ実行開始");
        await Task.Delay(300, cancellationToken);
        var result = $"{Name}: PostProcessing完了 at {DateTime.Now:HH:mm:ss}";
        Console.WriteLine($"[{Name}] {result}");
        return result;
    }
}
