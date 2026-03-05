using System.Collections.Concurrent;

namespace PluginManager;

/// <summary>
/// プラグイン間で共有される実行コンテキストを表します。
/// </summary>
public sealed class PluginContext
{
    /// <summary>
    /// 共有プロパティを指定してコンテキストを生成します。
    /// </summary>
    /// <param name="properties">初期プロパティ。未指定の場合は空の辞書。</param>
    public PluginContext(IDictionary<string, object?>? properties = null)
    {
        Properties = properties is null
            ? new ConcurrentDictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new ConcurrentDictionary<string, object?>(properties, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// プラグインで共有されるキー・バリューのプロパティ辞書を取得します。
    /// </summary>
    public ConcurrentDictionary<string, object?> Properties { get; }

    /// <summary>
    /// 指定キーの値を型付きで取得します。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="key">プロパティキー。</param>
    /// <returns>値が存在し型が一致する場合はその値。それ以外は既定値。</returns>
    public T? GetProperty<T>(string key)
        => Properties.TryGetValue(key, out var value) && value is T typed ? typed : default;

    /// <summary>
    /// 指定キーに値を設定します。キーが既に存在する場合は上書きします。
    /// </summary>
    /// <param name="key">プロパティキー。</param>
    /// <param name="value">設定する値。</param>
    public void SetProperty(string key, object? value) => Properties[key] = value;

    /// <summary>
    /// 指定キーのプロパティを削除します。
    /// 常駐プログラムで実行ごとに一意なキーを追加する場合は、
    /// 不要になった時点でこのメソッドで削除してください。
    /// </summary>
    /// <param name="key">削除するプロパティキー。</param>
    /// <returns>キーが存在し削除に成功した場合は <see langword="true"/>。それ以外は <see langword="false"/>。</returns>
    public bool RemoveProperty(string key) => Properties.TryRemove(key, out _);

    /// <summary>
    /// すべてのプロパティを削除します。
    /// 常駐プログラムで処理サイクルをまたいで同じインスタンスを再利用する場合に使用します。
    /// </summary>
    public void Clear() => Properties.Clear();

    /// <summary>
    /// 現在のコンテキストのプロパティをコピーした独立したスコープを生成します。
    /// 常駐プログラムで実行ごとに独立したコンテキストが必要な場合に使用します。
    /// </summary>
    /// <returns>プロパティのスナップショットを持つ新しい <see cref="PluginContext"/>。</returns>
    public PluginContext CreateScope()
        => new(new Dictionary<string, object?>(Properties, StringComparer.OrdinalIgnoreCase));
}
