using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace PluginManager;

/// <summary>
/// プラグインステージの登録と管理を行うレジストリです。
/// </summary>
/// <remarks>
/// <para>
/// このクラスは、フレームワーク標準ステージとカスタムステージを一元管理します。
/// </para>
/// <para>
/// <b>標準ステージ:</b>
/// <list type="bullet">
/// <item><see cref="PluginStage.PreProcessing"/>: プログラム開始前の前処理</item>
/// <item><see cref="PluginStage.Processing"/>: メイン処理</item>
/// <item><see cref="PluginStage.PostProcessing"/>: プログラム終了後の後処理</item>
/// </list>
/// </para>
/// <para>
/// <b>カスタムステージ:</b><br/>
/// 独自のステージを登録することで、ドメイン固有の処理フローを構築できます。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // カスタムステージの登録
/// var validation = PluginStageRegistry.Register("Validation", "データ検証");
/// var enrichment = PluginStageRegistry.Register("Enrichment", "データ加工");
/// 
/// // プラグインで使用
/// public IReadOnlySet&lt;PluginStage&gt; SupportedStages { get; } =
///     new[] { validation, enrichment }.ToFrozenSet();
/// </code>
/// </example>
public static class PluginStageRegistry
{
    private static readonly ConcurrentDictionary<string, PluginStageInfo> _stages = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();
    private static FrozenSet<PluginStage>? _allStagesCache;

    static PluginStageRegistry()
    {
        // 標準ステージを登録
        RegisterCore(PluginStage.PreProcessing, "プログラム開始前の前処理ステージ");
        RegisterCore(PluginStage.Processing, "メイン処理のステージ");
        RegisterCore(PluginStage.PostProcessing, "プログラム終了後の後処理ステージ");
    }

    /// <summary>
    /// カスタムステージを登録します。
    /// </summary>
    /// <param name="id">ステージを一意に識別するID。大文字小文字を区別しません。</param>
    /// <param name="description">ステージの説明。</param>
    /// <returns>登録されたステージ。</returns>
    /// <exception cref="ArgumentException">id が空白の場合。</exception>
    /// <exception cref="InvalidOperationException">同じIDのステージが既に登録されている場合。</exception>
    /// <example>
    /// <code>
    /// var validation = PluginStageRegistry.Register("Validation", "データ検証");
    /// var enrichment = PluginStageRegistry.Register("Enrichment", "データ加工");
    /// </code>
    /// </example>
    public static PluginStage Register(string id, string description = "")
    {
        var stage = new PluginStage(id);

        if (!_stages.TryAdd(id, new PluginStageInfo(stage, description)))
        {
            throw new InvalidOperationException($"ステージ '{id}' は既に登録されています。");
        }

        // キャッシュをクリア
        lock (_lock)
        {
            _allStagesCache = null;
        }

        return stage;
    }

    /// <summary>
    /// ステージを登録します（既に登録されている場合は既存のステージを返します）。
    /// </summary>
    /// <param name="id">ステージを一意に識別するID。大文字小文字を区別しません。</param>
    /// <param name="description">ステージの説明。</param>
    /// <returns>登録されたステージ。既に登録されている場合は既存のステージ。</returns>
    /// <exception cref="ArgumentException">id が空白の場合。</exception>
    /// <example>
    /// <code>
    /// // 重複登録を気にせず使用できる
    /// var validation = PluginStageRegistry.RegisterOrGet("Validation", "データ検証");
    /// var validation2 = PluginStageRegistry.RegisterOrGet("Validation", "データ検証");
    /// Assert.True(ReferenceEquals(validation, validation2));
    /// </code>
    /// </example>
    public static PluginStage RegisterOrGet(string id, string description = "")
    {
        if (TryGet(id, out var existing))
            return existing;

        return Register(id, description);
    }

    /// <summary>
    /// 指定されたIDのステージが登録されているかどうかを確認します。
    /// </summary>
    /// <param name="id">ステージID。</param>
    /// <returns>登録されている場合は <see langword="true"/>。</returns>
    public static bool IsRegistered(string id)
        => _stages.ContainsKey(id);

    /// <summary>
    /// 指定されたIDのステージを取得します。
    /// </summary>
    /// <param name="id">ステージID。</param>
    /// <param name="stage">取得したステージ。</param>
    /// <returns>ステージが見つかった場合は <see langword="true"/>。</returns>
    public static bool TryGet(string id, out PluginStage stage)
    {
        if (_stages.TryGetValue(id, out var info))
        {
            stage = info.Stage;
            return true;
        }

        stage = null!;
        return false;
    }

    /// <summary>
    /// 指定されたIDのステージを取得します。
    /// </summary>
    /// <param name="id">ステージID。</param>
    /// <returns>ステージ。</returns>
    /// <exception cref="InvalidOperationException">ステージが見つからない場合。</exception>
    public static PluginStage Get(string id)
    {
        if (!TryGet(id, out var stage))
            throw new InvalidOperationException($"ステージ '{id}' は登録されていません。");

        return stage;
    }

    /// <summary>
    /// 登録されているすべてのステージを取得します。
    /// </summary>
    /// <returns>すべてのステージ。</returns>
    public static FrozenSet<PluginStage> GetAll()
    {
        if (_allStagesCache is not null)
            return _allStagesCache;

        lock (_lock)
        {
            _allStagesCache ??= _stages.Values.Select(info => info.Stage).ToFrozenSet();
            return _allStagesCache;
        }
    }

    /// <summary>
    /// 指定されたIDのステージの説明を取得します。
    /// </summary>
    /// <param name="id">ステージID。</param>
    /// <returns>ステージの説明。見つからない場合は空文字列。</returns>
    public static string GetDescription(string id)
    {
        if (_stages.TryGetValue(id, out var info))
            return info.Description;

        return string.Empty;
    }

    /// <summary>
    /// すべてのステージ情報を取得します。
    /// </summary>
    /// <returns>ステージ情報の一覧。</returns>
    public static IReadOnlyDictionary<string, PluginStageInfo> GetAllInfo()
        => _stages.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 内部登録用メソッド。
    /// </summary>
    private static void RegisterCore(PluginStage stage, string description)
    {
        _stages.TryAdd(stage.Id, new PluginStageInfo(stage, description));
    }
}

/// <summary>
/// プラグインステージの情報を保持します。
/// </summary>
/// <param name="Stage">ステージ。</param>
/// <param name="Description">ステージの説明。</param>
public sealed record PluginStageInfo(PluginStage Stage, string Description);
