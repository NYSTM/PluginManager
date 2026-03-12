namespace PluginManager;

/// <summary>
/// <see cref="PluginExecutor"/> が発行する実行通知の種別です。
/// </summary>
public enum PluginExecutorNotificationType
{
    /// <summary>実行グループの開始。</summary>
    GroupStart,
    /// <summary>実行グループの完了。</summary>
    GroupCompleted,
    /// <summary>個別プラグイン実行の開始。</summary>
    PluginExecuteStart,
    /// <summary>個別プラグイン実行の成功。</summary>
    PluginExecuteCompleted,
    /// <summary>個別プラグイン実行の失敗。</summary>
    PluginExecuteFailed,
    /// <summary>個別プラグイン実行のスキップ。</summary>
    PluginSkipped,
}
