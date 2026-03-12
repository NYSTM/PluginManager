namespace PluginManager;

/// <summary>
/// プラグインのメタデータを提供するインターフェースです。
/// </summary>
/// <remarks>
/// <para>
/// このインターフェースは、プラグインの識別情報とライフサイクル情報を提供します。
/// </para>
/// <para>
/// <b>Single Responsibility Principle (SRP) に基づく設計:</b><br/>
/// メタデータ提供の責務のみを持ちます。初期化や実行の責務は他のインターフェースが担います。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class MyPlugin : IPluginMetadata
/// {
///     public string Id => "my-plugin";
///     public string Name => "マイプラグイン";
///     public Version Version => new(1, 0, 0);
///     public IReadOnlySet&lt;PluginStage&gt; SupportedStages { get; } =
///         new[] { PluginStage.Processing }.ToFrozenSet();
/// }
/// </code>
/// </example>
public interface IPluginMetadata
{
    /// <summary>
    /// プラグインを一意に識別するIDを取得します。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ID は大文字小文字を区別せず、プラグイン設定ファイル（pluginsettings.json）の
    /// <c>Id</c> フィールドと一致する必要があります。
    /// </para>
    /// <para>
    /// <b>命名規則:</b>
    /// <list type="bullet">
    /// <item>ケバブケース推奨: <c>my-plugin</c></item>
    /// <item>ドメイン名形式: <c>com.example.my-plugin</c></item>
    /// <item>一意性を確保すること</item>
    /// </list>
    /// </para>
    /// </remarks>
    string Id { get; }

    /// <summary>
    /// プラグインの表示名を取得します。
    /// </summary>
    /// <remarks>
    /// UI やログで表示される人間が読める名前です。
    /// </remarks>
    string Name { get; }

    /// <summary>
    /// プラグインのバージョンを取得します。
    /// </summary>
    /// <remarks>
    /// <para>
    /// バージョンはセマンティックバージョニング（SemVer）に従うことを推奨します。
    /// </para>
    /// <para>
    /// <b>例:</b>
    /// <list type="bullet">
    /// <item><c>new Version(1, 0, 0)</c>: 初回リリース</item>
    /// <item><c>new Version(1, 1, 0)</c>: 機能追加</item>
    /// <item><c>new Version(2, 0, 0)</c>: 破壊的変更</item>
    /// </list>
    /// </para>
    /// </remarks>
    Version Version { get; }

    /// <summary>
    /// プラグインが実行対象とするライフサイクルステージ一覧を取得します。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="System.Collections.Frozen.FrozenSet{T}"/> を使用することで、
    /// <c>Contains</c> 操作が O(1) で動作します。
    /// </para>
    /// <para>
    /// <b>標準ステージ:</b>
    /// <list type="bullet">
    /// <item><see cref="PluginStage.PreProcessing"/>: 前処理</item>
    /// <item><see cref="PluginStage.Processing"/>: メイン処理</item>
    /// <item><see cref="PluginStage.PostProcessing"/>: 後処理</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>カスタムステージ:</b><br/>
    /// <see cref="PluginStageRegistry"/> を使用して独自のステージを登録できます。
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 標準ステージ
    /// public IReadOnlySet&lt;PluginStage&gt; SupportedStages { get; } =
    ///     new[] { PluginStage.Processing }.ToFrozenSet();
    /// 
    /// // カスタムステージ
    /// public IReadOnlySet&lt;PluginStage&gt; SupportedStages { get; } =
    ///     new[] { MyStages.Validation, MyStages.Enrichment }.ToFrozenSet();
    /// </code>
    /// </example>
    IReadOnlySet<PluginStage> SupportedStages { get; }
}
