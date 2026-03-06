namespace PluginManager;

/// <summary>
/// プラグインとして読み込まれる機能の共通契約を定義します。
/// </summary>
public interface IPlugin
{
    /// <summary>プラグインを一意に識別するIDを取得します。</summary>
    string Id { get; }

    /// <summary>プラグインの表示名を取得します。</summary>
    string Name { get; }

    /// <summary>プラグインのバージョンを取得します。</summary>
    Version Version { get; }

    /// <summary>プラグインが実行対象とするライフサイクルステージ一覧を取得します。</summary>
    IReadOnlySet<PluginStage> SupportedStages { get; }

    /// <summary>プラグインを初期化します。</summary>
    /// <param name="context">初期化に利用する実行コンテキスト。</param>
    /// <param name="cancellationToken">初期化処理のキャンセル通知。</param>
    Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定ステージでプラグインを非同期実行し、結果を返します。
    /// </summary>
    /// <param name="stage">実行するライフサイクルステージ。</param>
    /// <param name="context">実行コンテキスト。</param>
    /// <param name="cancellationToken">キャンセル通知。</param>
    /// <returns>プラグインの実行結果。結果がない場合は <see langword="null"/>。</returns>
    Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default);
}
