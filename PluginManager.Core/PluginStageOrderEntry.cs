using System.Text.Json.Serialization;

namespace PluginManager;

/// <summary>
/// ステージごとのプラグイン実行順序設定を表します。
/// </summary>
public sealed class PluginStageOrderEntry
{
    /// <summary>
    /// 対象のライフサイクルステージを取得します。
    /// 標準値: "PreProcessing" / "Processing" / "PostProcessing"
    /// 独自ステージIDも指定可能です（例: "Validation", "Cleanup"）。
    /// </summary>
    [JsonConverter(typeof(PluginStageJsonConverter))]
    public PluginStage? Stage { get; init; }

    /// <summary>
    /// このステージで実行するプラグインの順序定義を取得します。
    /// </summary>
    public IReadOnlyList<PluginOrderEntry> PluginOrder { get; init; } = [];
}
