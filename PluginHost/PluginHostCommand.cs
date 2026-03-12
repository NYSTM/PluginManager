namespace PluginHost;

/// <summary>
/// プラグインホストプロセスへの要求種別を表します。
/// </summary>
internal enum PluginHostCommand
{
    /// <summary>プラグインをロードします。</summary>
    Load = 1,

    /// <summary>プラグインを初期化します。</summary>
    Initialize = 2,

    /// <summary>プラグインを実行します。</summary>
    Execute = 3,

    /// <summary>プラグインをアンロードします。</summary>
    Unload = 4,

    /// <summary>ホストプロセスを終了します。</summary>
    Shutdown = 5,

    /// <summary>ヘルスチェック（接続確認）を行います。</summary>
    Ping = 6,
}
