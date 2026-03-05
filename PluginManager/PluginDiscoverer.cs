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
    /// <returns>設定順序で並び替えられた <see cref="PluginDescriptor"/> の一覧。</returns>
    public IReadOnlyList<PluginDescriptor> DiscoverFromConfiguration(string configurationFilePath, string searchPattern = "*.dll")
    {
        var config = PluginConfigurationLoader.Load(configurationFilePath);
        if (string.IsNullOrWhiteSpace(config.PluginsPath))
            return [];

        return PluginOrderResolver.OrderByConfiguration(Discover(config.PluginsPath, searchPattern), config.StageOrders);
    }

    /// <summary>
    /// 指定ディレクトリからプラグイン DLL を探索し、<see cref="PluginDescriptor"/> の一覧を返します。
    /// </summary>
    /// <param name="directoryPath">探索対象のディレクトリパス。</param>
    /// <param name="searchPattern">DLL 検索パターン。既定値は <c>*.dll</c>。</param>
    /// <returns>発見した <see cref="PluginDescriptor"/> の一覧。</returns>
    /// <exception cref="ArgumentException"><paramref name="directoryPath"/> が空白の場合。</exception>
    public IReadOnlyList<PluginDescriptor> Discover(string directoryPath, string searchPattern = "*.dll")
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("ディレクトリパスが必要です。", nameof(directoryPath));

        if (!Directory.Exists(directoryPath))
            return [];

        var descriptors = new List<PluginDescriptor>();
        foreach (var file in Directory.EnumerateFiles(directoryPath, searchPattern, SearchOption.TopDirectoryOnly))
            TryDiscoverFromAssembly(file, descriptors);

        return descriptors;
    }

    /// <summary>
    /// 1 つのアセンブリファイルを一時コンテキストでロードし、
    /// <see cref="IPlugin"/> 実装型を <paramref name="descriptors"/> に追加します。
    /// ロードに失敗したアセンブリは無視します。
    /// </summary>
    private void TryDiscoverFromAssembly(string assemblyPath, List<PluginDescriptor> descriptors)
    {
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

            return new PluginDescriptor(attribute.Id, attribute.Name, parsed, pluginType, assemblyPath, stages);
        }

        var fallbackVersion = pluginType.Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
        return new PluginDescriptor(
            pluginType.FullName ?? pluginType.Name,
            pluginType.Name,
            fallbackVersion,
            pluginType,
            assemblyPath,
            _defaultStages);
    }
}
