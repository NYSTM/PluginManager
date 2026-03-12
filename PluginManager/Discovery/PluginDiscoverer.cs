using System.Collections.Frozen;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace PluginManager;

/// <summary>
/// 指定ディレクトリからプラグインを探索します。
/// </summary>
internal sealed class PluginDiscoverer(ILogger? logger = null)
{
    private readonly ILogger? _logger = logger;

    /// <summary>
    /// <see cref="PluginAttribute"/> にステージ指定がない場合に適用するデフォルトステージセット。
    /// PreProcessing / Processing / PostProcessing の標準 3 ステージを含みます。
    /// </summary>
    private static readonly FrozenSet<PluginStage> _defaultStages =
        new[] { PluginStage.PreProcessing, PluginStage.Processing, PluginStage.PostProcessing }
        .ToFrozenSet();

    /// <summary>
    /// 設定ファイルに従いプラグインを探索し、設定順序付きで返します。
    /// </summary>
    /// <param name="configurationFilePath">設定ファイルのパス。</param>
    /// <param name="searchPattern">DLL 検索パターン。既定値は <c>*.dll</c>。</param>
    /// <param name="cancellationToken">探索のキャンセル通知。</param>
    /// <returns>設定順序で並び替えられた <see cref="PluginDescriptor"/> の一覧。</returns>
    public IReadOnlyList<PluginDescriptor> DiscoverFromConfiguration(
        string configurationFilePath, 
        string searchPattern = "*.dll",
        CancellationToken cancellationToken = default)
    {
        var config = PluginConfigurationLoader.Load(configurationFilePath);
        if (string.IsNullOrWhiteSpace(config.PluginsPath))
            return [];

        return PluginOrderResolver.OrderByConfiguration(
            Discover(config.PluginsPath, searchPattern, cancellationToken), 
            config.StageOrders, 
            config.PluginDependencies);
    }

    /// <summary>
    /// 指定ディレクトリからプラグイン DLL を探索し、<see cref="PluginDescriptor"/> の一覧を返します。
    /// DLL ファイルのスキャンは並列で実行されます。
    /// </summary>
    /// <param name="directoryPath">探索対象のディレクトリパス。</param>
    /// <param name="searchPattern">DLL 検索パターン。既定値は <c>*.dll</c>。</param>
    /// <param name="cancellationToken">探索のキャンセル通知。</param>
    /// <returns>発見した <see cref="PluginDescriptor"/> の一覧。</returns>
    /// <exception cref="ArgumentException"><paramref name="directoryPath"/> が空白の場合。</exception>
    /// <exception cref="InvalidOperationException">同じ ID を持つプラグインが複数発見された場合。</exception>
    /// <exception cref="OperationCanceledException">キャンセルされた場合。</exception>
    public IReadOnlyList<PluginDescriptor> Discover(
        string directoryPath, 
        string searchPattern = "*.dll",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("ディレクトリパスが必要です。", nameof(directoryPath));

        if (!Directory.Exists(directoryPath))
            return [];

        var files = Directory.EnumerateFiles(directoryPath, searchPattern, SearchOption.TopDirectoryOnly);

        // 各ファイルを並列処理し、それぞれのスレッドで独立したリストを作成
        var allDescriptors = files
            .AsParallel()
            .WithCancellation(cancellationToken)
            .SelectMany(file => DiscoverFromAssembly(file))
            .ToList();

        // プラグイン ID の重複をチェック
        ValidateNoDuplicates(allDescriptors);

        return allDescriptors;
    }

    /// <summary>
    /// 1 つのアセンブリファイルを一時コンテキストでロードし、
    /// <see cref="IPlugin"/> 実装型のリストを返します。
    /// ロードに失敗した場合は空のリストを返します。
    /// </summary>
    private IEnumerable<PluginDescriptor> DiscoverFromAssembly(string assemblyPath)
    {
        var descriptors = new List<PluginDescriptor>();
        PluginLoadContext? loadContext = null;
        try
        {
            loadContext = new PluginLoadContext(assemblyPath);
            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(IPlugin).IsAssignableFrom(type))
                    continue;

                var descriptor = CreateDescriptor(type, assemblyPath);
                if (descriptor is not null)
                    descriptors.Add(descriptor);
            }
        }
        catch (Exception ex)
        {
            // 壊れたアセンブリや依存関係不備は探索をスキップし、原因をデバッグログに記録する
            _logger?.LogDebug(ex, "アセンブリのスキャンをスキップしました。Path={AssemblyPath}", assemblyPath);
        }
        finally
        {
            loadContext?.Unload();
        }
        
        return descriptors;
    }

    /// <summary>
    /// プラグイン型から <see cref="PluginDescriptor"/> を生成します。
    /// <see cref="PluginAttribute"/> が付与されている場合はその値を優先します。
    /// </summary>
    private static PluginDescriptor? CreateDescriptor(Type pluginType, string assemblyPath)
    {
        var attribute = pluginType.GetCustomAttribute<PluginAttribute>();
        if (attribute is not null)
        {
            if (!Version.TryParse(attribute.Version, out var parsed))
                parsed = new Version(1, 0, 0, 0);

            var stages = attribute.SupportedStageIds.Length > 0
                ? (IReadOnlySet<PluginStage>)attribute.SupportedStageIds
                    .Select(id => new PluginStage(id))
                    .ToFrozenSet()
                : _defaultStages;

            return new PluginDescriptor(attribute.Id, attribute.Name, parsed, pluginType.FullName ?? pluginType.Name, assemblyPath, stages)
            {
                IsolationMode = attribute.IsolationMode,
            };
        }

        var fallbackVersion = pluginType.Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
        return new PluginDescriptor(
            pluginType.FullName ?? pluginType.Name,
            pluginType.Name,
            fallbackVersion,
            pluginType.FullName ?? pluginType.Name,
            assemblyPath,
            _defaultStages);
    }

    /// <summary>
    /// プラグイン ID の重複を検証します。
    /// </summary>
    /// <param name="descriptors">検証対象のプラグイン記述子リスト。</param>
    /// <exception cref="InvalidOperationException">同じ ID を持つプラグインが複数存在する場合。</exception>
    private static void ValidateNoDuplicates(List<PluginDescriptor> descriptors)
    {
        var duplicates = descriptors
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicates.Count > 0)
        {
            var duplicateIds = string.Join(", ", duplicates.Select(g => $"'{g.Key}'"));
            var details = string.Join("; ", duplicates.Select(g =>
                $"{g.Key}: {string.Join(", ", g.Select(d => Path.GetFileName(d.AssemblyPath)))}"));

            throw new InvalidOperationException(
                $"プラグイン ID が重複しています: {duplicateIds}。詳細: {details}");
        }
    }
}
