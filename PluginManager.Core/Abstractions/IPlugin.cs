namespace PluginManager;

/// <summary>
/// プラグインとして読み込まれる機能の共通契約を定義します。
/// </summary>
/// <remarks>
/// <para>
/// このインターフェースは、以下の責務を組み合わせています：
/// <list type="bullet">
/// <item><see cref="IPluginMetadata"/>: メタデータ提供</item>
/// <item><see cref="IPluginInitializer"/>: 初期化</item>
/// <item><see cref="IStageExecutor"/>: ステージ実行</item>
/// </list>
/// </para>
/// <para>
/// <b>Single Responsibility Principle (SRP) に基づく設計:</b><br/>
/// 各責務は独立したインターフェースとして定義されており、
/// <see cref="IPlugin"/> はそれらを組み合わせた複合インターフェースです。
/// </para>
/// <para>
/// <b>実装の選択肢:</b>
/// <list type="number">
/// <item><see cref="IPlugin"/> を直接実装する（すべての責務を持つ）</item>
/// <item>個別のインターフェース（<see cref="IPluginMetadata"/> など）だけを実装する</item>
/// <item><see cref="PluginBase"/> を継承する（推奨、便利な基底クラス）</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 方法1: IPlugin を直接実装
/// public sealed class MyPlugin : IPlugin
/// {
///     public string Id => "my-plugin";
///     public string Name => "マイプラグイン";
///     public Version Version => new(1, 0, 0);
///     public IReadOnlySet&lt;PluginStage&gt; SupportedStages { get; } =
///         new[] { PluginStage.Processing }.ToFrozenSet();
///     
///     public async Task InitializeAsync(PluginContext context, CancellationToken ct)
///     {
///         // 初期化処理
///     }
///     
///     public async Task&lt;object?&gt; ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken ct)
///     {
///         // 実行処理
///         return "完了";
///     }
/// }
/// 
/// // 方法2: 個別のインターフェースを実装
/// public sealed class MetadataOnlyPlugin : IPluginMetadata
/// {
///     public string Id => "metadata-only";
///     public string Name => "メタデータのみ";
///     public Version Version => new(1, 0, 0);
///     public IReadOnlySet&lt;PluginStage&gt; SupportedStages { get; } =
///         new[] { PluginStage.Processing }.ToFrozenSet();
/// }
/// 
/// // 方法3: PluginBase を継承（推奨）
/// public sealed class SimplePlugin : PluginBase
/// {
///     public SimplePlugin()
///         : base("simple-plugin", "シンプルプラグイン", new Version(1, 0, 0), PluginStage.Processing)
///     {
///     }
///     
///     protected override async Task&lt;object?&gt; OnExecuteAsync(PluginStage stage, PluginContext context, CancellationToken ct)
///     {
///         // 実行処理のみ実装
///         return "完了";
///     }
/// }
/// </code>
/// </example>
public interface IPlugin : IPluginMetadata, IPluginInitializer, IStageExecutor
{
    // このインターフェースは、上記3つのインターフェースを組み合わせた複合インターフェースです。
    // メンバーは継承元のインターフェースから提供されます。
}
