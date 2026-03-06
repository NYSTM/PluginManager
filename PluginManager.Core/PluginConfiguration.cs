namespace PluginManager;

using System.Collections.Frozen;

/// <summary>
/// プラグイン読み込み設定を表します。
/// </summary>
public sealed class PluginConfiguration
{
    private FrozenDictionary<PluginStage, IReadOnlyList<PluginOrderEntry>>? _stageOrderCache;

    /// <summary>
    /// プラグイン DLL を探索するディレクトリパスを取得します。
    /// </summary>
    public string? PluginsPath { get; init; }

    /// <summary>
    /// ステージごとのプラグイン実行順序の定義を取得します。
    /// </summary>
    public IReadOnlyList<PluginStageOrderEntry> StageOrders
    {
        get => _stageOrders;
        init
        {
            _stageOrders = value;
            _stageOrderCache = null; // キャッシュをリセット（初回 GetPluginOrder 呼び出し時に再構築）
        }
    }
    private IReadOnlyList<PluginStageOrderEntry> _stageOrders = [];

    /// <summary>
    /// プラグイン読み込み間の待機時間（ミリ秒）を取得します。<c>0</c> で待機なし。
    /// </summary>
    public int IntervalMilliseconds { get; init; }

    /// <summary>
    /// プラグイン読み込みのタイムアウト時間（ミリ秒）を取得します。<c>0</c> でタイムアウトなし。
    /// </summary>
    public int TimeoutMilliseconds { get; init; }

    /// <summary>
    /// ロード失敗時のリトライ回数を取得します。<c>0</c> でリトライなし。
    /// </summary>
    public int RetryCount { get; init; }

    /// <summary>
    /// リトライ間の待機時間（ミリ秒）を取得します。既定値は <c>500</c>。
    /// </summary>
    public int RetryDelayMilliseconds { get; init; } = 500;

    /// <summary>
    /// 指定ステージの <see cref="PluginOrderEntry"/> 一覧を O(1) で取得します。
    /// </summary>
    /// <param name="stage">対象ステージ。</param>
    /// <returns>該当ステージの順序定義。存在しない場合は空リスト。</returns>
    public IReadOnlyList<PluginOrderEntry> GetPluginOrder(PluginStage stage)
        => GetOrBuildCache().TryGetValue(stage, out var order) ? order : [];

    /// <summary>
    /// <see cref="PluginsPath"/> が相対パスの場合、<paramref name="basePath"/> を起点に
    /// 絶対パスへ変換した新しい <see cref="PluginConfiguration"/> を返します。
    /// 既に絶対パスの場合はそのまま返します。
    /// </summary>
    /// <param name="basePath">相対パスの基準ディレクトリ（通常は設定ファイルのディレクトリ）。</param>
    internal PluginConfiguration ResolvePluginsPath(string basePath)
    {
        if (string.IsNullOrWhiteSpace(PluginsPath) || Path.IsPathRooted(PluginsPath))
            return this;

        return new PluginConfiguration
        {
            PluginsPath            = Path.GetFullPath(PluginsPath, basePath),
            StageOrders            = StageOrders,
            IntervalMilliseconds   = IntervalMilliseconds,
            TimeoutMilliseconds    = TimeoutMilliseconds,
            RetryCount             = RetryCount,
            RetryDelayMilliseconds = RetryDelayMilliseconds,
        };
    }

    /// <summary>
    /// 指定した設定ファイルから <see cref="PluginConfiguration"/> を読み込みます。
    /// </summary>
    /// <param name="configurationFilePath">設定ファイルのパス。</param>
    /// <returns>読み込んだプラグイン設定。</returns>
    public static PluginConfiguration Load(string configurationFilePath)
        => PluginConfigurationLoader.Load(configurationFilePath);

    private FrozenDictionary<PluginStage, IReadOnlyList<PluginOrderEntry>> GetOrBuildCache()
    {
        if (_stageOrderCache is not null)
            return _stageOrderCache;

        var dict = new Dictionary<PluginStage, IReadOnlyList<PluginOrderEntry>>();
        foreach (var e in _stageOrders)
        {
            if (e.Stage is not null)
                dict[e.Stage] = e.PluginOrder;
        }
        return _stageOrderCache = dict.ToFrozenDictionary();
    }
}
