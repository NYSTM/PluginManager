using System.Collections.Concurrent;
using System.Text.Json;

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
    /// <remarks>
    /// キーが存在しない場合と型が不一致の場合を区別したい場合は <see cref="TryGetProperty{T}(string, out T)"/> を使用してください。
    /// </remarks>
    public T? GetProperty<T>(string key)
        => TryGetProperty<T>(key, out var value) ? value : default;

    /// <summary>
    /// 型安全なキーを使用して値を取得します。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="key">型安全なコンテキストキー。</param>
    /// <returns>値が存在し型が一致する場合はその値。それ以外は既定値。</returns>
    public T? GetProperty<T>(ContextKey<T> key)
        => GetProperty<T>(key.Name);

    /// <summary>
    /// 指定キーの値を型付きで取得を試みます。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="key">プロパティキー。</param>
    /// <param name="value">取得に成功した場合は値、失敗した場合は既定値。</param>
    /// <returns>キーが存在し型が一致する場合は <see langword="true"/>。それ以外は <see langword="false"/>。</returns>
    /// <example>
    /// <code>
    /// if (context.TryGetProperty&lt;int&gt;("count", out var count))
    /// {
    ///     Console.WriteLine($"count = {count}");
    /// }
    /// else
    /// {
    ///     Console.WriteLine("count が存在しないか、型が不一致");
    /// }
    /// </code>
    /// </example>
    public bool TryGetProperty<T>(string key, out T? value)
    {
        if (Properties.TryGetValue(key, out var obj))
        {
            // null は参照型として扱う（値型の場合は失敗）
            if (obj is null)
            {
                if (default(T) is null)  // 参照型または Nullable<T>
                {
                    value = default;
                    return true;
                }
                // 値型の場合は null を受け入れない
                value = default;
                return false;
            }

            if (obj is T typed)
            {
                value = typed;
                return true;
            }
        }
        
        value = default;
        return false;
    }

    /// <summary>
    /// 型安全なキーを使用して値の取得を試みます。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="key">型安全なコンテキストキー。</param>
    /// <param name="value">取得に成功した場合は値、失敗した場合は既定値。</param>
    /// <returns>キーが存在し型が一致する場合は <see langword="true"/>。それ以外は <see langword="false"/>。</returns>
    public bool TryGetProperty<T>(ContextKey<T> key, out T? value)
        => TryGetProperty(key.Name, out value);

    /// <summary>
    /// 指定キーの値を型付きで取得します。取得に失敗した場合は指定されたデフォルト値を返します。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="key">プロパティキー。</param>
    /// <param name="defaultValue">取得に失敗した場合のデフォルト値。</param>
    /// <returns>値が存在し型が一致する場合はその値。それ以外はデフォルト値。</returns>
    /// <example>
    /// <code>
    /// var count = context.GetPropertyOrDefault("count", 100);  // 存在しない場合は 100
    /// var name = context.GetPropertyOrDefault("name", "Unknown");  // 存在しない場合は "Unknown"
    /// </code>
    /// </example>
    public T GetPropertyOrDefault<T>(string key, T defaultValue)
        => TryGetProperty<T>(key, out var value) ? value! : defaultValue;

    /// <summary>
    /// 型安全なキーを使用して値を取得します。取得に失敗した場合は指定されたデフォルト値を返します。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="key">型安全なコンテキストキー。</param>
    /// <param name="defaultValue">取得に失敗した場合のデフォルト値。</param>
    /// <returns>値が存在し型が一致する場合はその値。それ以外はデフォルト値。</returns>
    public T GetPropertyOrDefault<T>(ContextKey<T> key, T defaultValue)
        => GetPropertyOrDefault(key.Name, defaultValue);

    /// <summary>
    /// 指定キーの値を型付きで取得します。キーが存在しない場合や型が不一致の場合は例外をスローします。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="key">プロパティキー。</param>
    /// <returns>指定した型の値。</returns>
    /// <exception cref="KeyNotFoundException">指定キーが存在しない場合。</exception>
    /// <exception cref="InvalidCastException">値の型が T に変換できない場合。</exception>
    /// <example>
    /// <code>
    /// try
    /// {
    ///     var count = context.GetPropertyOrThrow&lt;int&gt;("count");
    ///     Console.WriteLine($"count = {count}");
    /// }
    /// catch (KeyNotFoundException)
    /// {
    ///     Console.WriteLine("キーが存在しません");
    /// }
    /// catch (InvalidCastException)
    /// {
    ///     Console.WriteLine("型が不一致");
    /// }
    /// </code>
    /// </example>
    public T GetPropertyOrThrow<T>(string key)
    {
        if (!Properties.TryGetValue(key, out var value))
            throw new KeyNotFoundException($"プロパティキー '{key}' が見つかりません。");

        // null は参照型として扱う
        if (value is null)
        {
            if (default(T) is null)  // 参照型または Nullable<T>
                return default!;
            
            throw new InvalidCastException(
                $"プロパティ '{key}' の型 'null' を '{typeof(T).Name}' に変換できません。");
        }

        if (value is not T typed)
            throw new InvalidCastException(
                $"プロパティ '{key}' の型 '{value.GetType().Name}' を '{typeof(T).Name}' に変換できません。");

        return typed;
    }

    /// <summary>
    /// 型安全なキーを使用して値を取得します。キーが存在しない場合や型が不一致の場合は例外をスローします。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="key">型安全なコンテキストキー。</param>
    /// <returns>指定した型の値。</returns>
    /// <exception cref="KeyNotFoundException">指定キーが存在しない場合。</exception>
    /// <exception cref="InvalidCastException">値の型が T に変換できない場合。</exception>
    public T GetPropertyOrThrow<T>(ContextKey<T> key)
        => GetPropertyOrThrow<T>(key.Name);

    /// <summary>
    /// 指定キーに値を設定します。キーが既に存在する場合は上書きします。
    /// </summary>
    /// <param name="key">プロパティキー。</param>
    /// <param name="value">設定する値。</param>
    public void SetProperty(string key, object? value) => Properties[key] = value;

    /// <summary>
    /// 型安全なキーを使用して値を設定します。キーが既に存在する場合は上書きします。
    /// </summary>
    /// <typeparam name="T">設定する値の型。</typeparam>
    /// <param name="key">型安全なコンテキストキー。</param>
    /// <param name="value">設定する値。</param>
    public void SetProperty<T>(ContextKey<T> key, T value)
        => SetProperty(key.Name, value);

    /// <summary>
    /// すべてのコンテキストプロパティを取得します。
    /// </summary>
    /// <returns>キーと値のペアのコレクション。</returns>
    public IEnumerable<KeyValuePair<string, object?>> GetAllProperties()
        => Properties;

    /// <summary>
    /// 指定キーのプロパティを削除します。
    /// 常駐プログラムで実行ごとに一意なキーを追加する場合は、
    /// 不要になった時点でこのメソッドで削除してください。
    /// </summary>
    /// <param name="key">削除するプロパティキー。</param>
    /// <returns>キーが存在し削除に成功した場合は <see langword="true"/>。それ以外は <see langword="false"/>。</returns>
    public bool RemoveProperty(string key) => Properties.TryRemove(key, out _);

    /// <summary>
    /// 型安全なキーを使用してプロパティを削除します。
    /// </summary>
    /// <typeparam name="T">削除する値の型。</typeparam>
    /// <param name="key">型安全なコンテキストキー。</param>
    /// <returns>キーが存在し削除に成功した場合は <see langword="true"/>。それ以外は <see langword="false"/>。</returns>
    public bool RemoveProperty<T>(ContextKey<T> key)
        => RemoveProperty(key.Name);

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

    /// <summary>
    /// すべてのプロパティを <see cref="JsonElement"/> 値の辞書に変換します。
    /// プロセス間通信で型情報を保持したまま転送する際に使用します。
    /// </summary>
    /// <returns>キーと <see cref="JsonElement"/> 値のペアの辞書。</returns>
    public Dictionary<string, JsonElement> ToJsonDictionary()
    {
        var result = new Dictionary<string, JsonElement>(Properties.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in Properties)
            result[key] = JsonSerializer.SerializeToElement(value);
        return result;
    }

    /// <summary>
    /// <see cref="JsonElement"/> 値の辞書をコンテキストに適用します。
    /// 既存キーは上書きされます。型の復元は <see cref="JsonElement.ValueKind"/> に基づきます。
    /// </summary>
    /// <param name="data">適用する辞書。</param>
    public void ApplyJsonDictionary(IReadOnlyDictionary<string, JsonElement> data)
    {
        foreach (var (key, element) in data)
            Properties[key] = DeserializeJsonElement(element);
    }

    private static object? DeserializeJsonElement(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt32(out var i) => i,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String => element.GetString(),
            _ => element.Deserialize<object?>(),
        };
}
