namespace PluginManager;

/// <summary>
/// 型安全なコンテキストキーを表します。
/// </summary>
/// <typeparam name="T">このキーが扱う値の型。</typeparam>
/// <remarks>
/// <para>
/// <see cref="ContextKey{T}"/> を使用することで、コンパイル時に型安全性を保証できます。
/// </para>
/// <para>
/// <b>使用例:</b>
/// <code>
/// // キーの定義（静的フィールド推奨）
/// public static class MyKeys
/// {
///     public static readonly ContextKey&lt;int&gt; RequestCount = new("RequestCount");
///     public static readonly ContextKey&lt;string&gt; UserName = new("UserName");
/// }
/// 
/// // 値の設定
/// context.SetProperty(MyKeys.RequestCount, 42);
/// 
/// // 値の取得（型安全）
/// int count = context.GetProperty(MyKeys.RequestCount);  // コンパイル時に型チェック
/// </code>
/// </para>
/// </remarks>
public sealed class ContextKey<T>
{
    /// <summary>
    /// 指定した名前でキーを生成します。
    /// </summary>
    /// <param name="name">キー名。</param>
    public ContextKey(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// キー名を取得します。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// キー名の文字列表現を返します。
    /// </summary>
    public override string ToString() => Name;

    /// <summary>
    /// 文字列への暗黙的な変換を提供します。
    /// </summary>
    public static implicit operator string(ContextKey<T> key) => key.Name;
}
