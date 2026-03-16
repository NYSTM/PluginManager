namespace PluginManager;

using System.Text.Json;

/// <summary>
/// 設定ファイルから <see cref="PluginConfiguration"/> を読み込みます。
/// </summary>
public static class PluginConfigurationLoader
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// 指定した設定ファイルから <see cref="PluginConfiguration"/> を読み込みます。
    /// </summary>
    /// <param name="configurationFilePath">設定ファイルのパス。</param>
    /// <returns>読み込んだプラグイン設定。</returns>
    /// <exception cref="ArgumentException">設定ファイルパスが未指定の場合。</exception>
    /// <exception cref="FileNotFoundException">設定ファイルが存在しない場合。</exception>
    /// <exception cref="InvalidOperationException">設定のデシリアライズまたは値検証に失敗した場合。</exception>
    public static PluginConfiguration Load(string configurationFilePath)
    {
        if (string.IsNullOrWhiteSpace(configurationFilePath))
            throw new ArgumentException("設定ファイルのパスを指定してください。", nameof(configurationFilePath));

        // Environment.CurrentDirectory 依存を避けるため絶対パスに正規化する
        configurationFilePath = Path.GetFullPath(configurationFilePath);

        if (!File.Exists(configurationFilePath))
            throw new FileNotFoundException("設定ファイルが見つかりません。", configurationFilePath);

        var json = File.ReadAllText(configurationFilePath);
        var configuration = JsonSerializer.Deserialize<PluginConfiguration>(json, _jsonOptions)
            ?? throw new InvalidOperationException("プラグイン設定の読み込みに失敗しました。JSONの内容を確認してください。");

        Validate(configuration);

        // PluginsPath が相対パスの場合、設定ファイルのディレクトリを基準に絶対パスへ変換する。
        // Windows Service では Environment.CurrentDirectory が C:\Windows\System32 になる場合があるため
        // Path.GetDirectoryName(configurationFilePath) を起点とすることで確実にパスを解決する。
        return configuration.ResolvePluginsPath(
            Path.GetDirectoryName(configurationFilePath)!);
    }

    private static void Validate(PluginConfiguration configuration)
    {
        if (configuration.IntervalMilliseconds < 0)
            throw new InvalidOperationException("IntervalMilliseconds は 0 以上で指定してください。");

        if (configuration.TimeoutMilliseconds < 0)
            throw new InvalidOperationException("TimeoutMilliseconds は 0 以上で指定してください。");

        if (configuration.RetryCount < 0)
            throw new InvalidOperationException("RetryCount は 0 以上で指定してください。");

        if (configuration.RetryDelayMilliseconds < 0)
            throw new InvalidOperationException("RetryDelayMilliseconds は 0 以上で指定してください。");

        if (configuration.PluginHostShutdownTimeoutMilliseconds <= 0)
            throw new InvalidOperationException("PluginHostShutdownTimeoutMilliseconds は 1 以上で指定してください。");

        if (configuration.StageOrders is null)
            throw new InvalidOperationException("StageOrders は必須です。");

        for (int i = 0; i < configuration.StageOrders.Count; i++)
        {
            var stageOrder = configuration.StageOrders[i]
                ?? throw new InvalidOperationException($"StageOrders[{i}] が null です。");

            if (stageOrder.Stage is null)
                throw new InvalidOperationException($"StageOrders[{i}].Stage は必須です。");

            if (stageOrder.PluginOrder is null)
                throw new InvalidOperationException($"StageOrders[{i}].PluginOrder は必須です。");

            if (stageOrder.MaxDegreeOfParallelism is <= 0)
                throw new InvalidOperationException($"StageOrders[{i}].MaxDegreeOfParallelism は 1 以上で指定してください。");

            // ステージ内でのプラグイン ID の重複をチェック
            var pluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int j = 0; j < stageOrder.PluginOrder.Count; j++)
            {
                var entry = stageOrder.PluginOrder[j];
                if (string.IsNullOrWhiteSpace(entry.Id))
                    throw new InvalidOperationException($"StageOrders[{i}].PluginOrder[{j}].Id は必須です。");

                if (entry.Order < 0)
                    throw new InvalidOperationException($"StageOrders[{i}].PluginOrder[{j}].Order は 0 以上で指定してください。");

                // 重複チェック
                if (!pluginIds.Add(entry.Id))
                    throw new InvalidOperationException(
                        $"プラグイン ID '{entry.Id}' がステージ '{stageOrder.Stage}' 内で重複しています。");
            }
        }
    }
}
