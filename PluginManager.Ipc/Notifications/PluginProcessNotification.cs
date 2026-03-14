namespace PluginManager.Ipc;

/// <summary>
/// 別プロセス実行中の通知メッセージを表します。
/// </summary>
public sealed record PluginProcessNotification
{
    /// <summary>通知種別。</summary>
    public required PluginProcessNotificationType NotificationType { get; init; }

    /// <summary>通知メッセージ。</summary>
    public required string Message { get; init; }

    /// <summary>要求 ID。</summary>
    public string? RequestId { get; init; }

    /// <summary>プラグイン ID。</summary>
    public string? PluginId { get; init; }

    /// <summary>ステージ ID。</summary>
    public string? StageId { get; init; }

    /// <summary>ホストプロセス ID。</summary>
    public int ProcessId { get; init; }

    /// <summary>失敗時のエラー型名。</summary>
    public string? ErrorType { get; init; }

    /// <summary>失敗時のエラーメッセージ。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>生成時刻（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
}
