using System.Text.Json;

namespace PluginManager.Ipc;

/// <summary>
/// プラグインホストからの応答メッセージを表します。
/// </summary>
public sealed record PluginHostResponse
{
    /// <summary>要求 ID（対応する要求の ID）。</summary>
    public required string RequestId { get; init; }

    /// <summary>処理が成功したかどうか。</summary>
    public required bool Success { get; init; }

    /// <summary>エラーメッセージ（失敗時）。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>エラー型名（失敗時）。</summary>
    public string? ErrorType { get; init; }

    /// <summary>実行結果データ（JSON シリアライズ済み）。</summary>
    public JsonElement? ResultData { get; init; }

    /// <summary>更新されたコンテキストデータ（JSON 表現を保持したシリアライズ済みデータ）。</summary>
    public IReadOnlyDictionary<string, JsonElement>? ContextData { get; init; }
}
