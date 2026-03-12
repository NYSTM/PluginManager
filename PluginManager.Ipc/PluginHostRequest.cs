using System.Text.Json;

namespace PluginManager.Ipc;

/// <summary>
/// プラグインホストへの要求メッセージを表します。
/// </summary>
public sealed record PluginHostRequest
{
    /// <summary>要求 ID（応答とのマッチングに使用）。</summary>
    public required string RequestId { get; init; }

    /// <summary>コマンド種別。</summary>
    public required PluginHostCommand Command { get; init; }

    /// <summary>プラグイン ID。</summary>
    public string? PluginId { get; init; }

    /// <summary>アセンブリパス。</summary>
    public string? AssemblyPath { get; init; }

    /// <summary>プラグイン型のフルネーム。</summary>
    public string? PluginTypeName { get; init; }

    /// <summary>実行ステージ ID。</summary>
    public string? StageId { get; init; }

    /// <summary>コンテキストデータ（JSON 表現を保持したシリアライズ済みデータ）。</summary>
    public IReadOnlyDictionary<string, JsonElement>? ContextData { get; init; }
}
