namespace PluginManager;

/// <summary>
/// プラグイン実装クラスに付与するメタデータ属性です。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PluginAttribute : Attribute
{
    /// <summary>
    /// プラグイン属性を初期化します。
    /// </summary>
    /// <param name="id">プラグインを一意に識別するID。</param>
    /// <param name="name">プラグインの表示名。</param>
    /// <param name="version">プラグインのバージョン文字列。</param>
    /// <param name="supportedStageIds">
    /// サポートするステージID の一覧。省略時は標準 3 ステージすべてが対象。
    /// 独自ステージID も自由に指定できます（例: "Validation", "Cleanup"）。
    /// </param>
    public PluginAttribute(string id, string name, string version, params string[] supportedStageIds)
    {
        Id = id;
        Name = name;
        Version = version;
        SupportedStageIds = supportedStageIds.Length > 0
            ? supportedStageIds
            : [PluginStage.PreProcessing.Id, PluginStage.Processing.Id, PluginStage.PostProcessing.Id];
    }

    /// <summary>プラグインを一意に識別するIDを取得します。</summary>
    public string Id { get; }

    /// <summary>プラグインの表示名を取得します。</summary>
    public string Name { get; }

    /// <summary>プラグインのバージョン文字列を取得します。</summary>
    public string Version { get; }

    /// <summary>サポートするステージIDの一覧を取得します。</summary>
    public string[] SupportedStageIds { get; }
}
