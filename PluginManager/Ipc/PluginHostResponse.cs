using System.Text.Json;

namespace PluginManager.Ipc;

/// <summary>
/// プラグインホストからの応答メッセージを表します。
/// </summary>
internal sealed record PluginHostResponse
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
    public string? ResultData { get; init; }

    /// <summary>更新されたコンテキストデータ（型情報を保持した JSON シリアライズ済み）。</summary>
    public Dictionary<string, JsonElement>? ContextData { get; init; }
}
