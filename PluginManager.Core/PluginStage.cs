namespace PluginManager;

/// <summary>
/// プラグインのライフサイクルステージを表す値オブジェクトです。
/// フレームワーク標準ステージのほか、実装者が独自ステージを自由に定義できます。
/// </summary>
public sealed class PluginStage : IEquatable<PluginStage>
{
    /// <summary>プログラム開始前の前処理ステージ。</summary>
    public static readonly PluginStage PreProcessing = new("PreProcessing");

    /// <summary>メイン処理のステージ。</summary>
    public static readonly PluginStage Processing = new("Processing");

    /// <summary>プログラム終了後の後処理ステージ。</summary>
    public static readonly PluginStage PostProcessing = new("PostProcessing");

    /// <summary>
    /// ステージIDを指定してインスタンスを生成します。
    /// </summary>
    /// <param name="id">ステージを一意に識別するID。大文字小文字を区別しません。</param>
    /// <exception cref="ArgumentException">id が空白の場合。</exception>
    public PluginStage(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("ステージIDは必須です。", nameof(id));
        Id = id;
    }

    /// <summary>ステージを一意に識別するIDを取得します。</summary>
    public string Id { get; }

    /// <inheritdoc/>
    public bool Equals(PluginStage? other)
        => other is not null && StringComparer.OrdinalIgnoreCase.Equals(Id, other.Id);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PluginStage s && Equals(s);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Id);

    /// <inheritdoc/>
    public override string ToString() => Id;

    public static bool operator ==(PluginStage? left, PluginStage? right)
        => left?.Equals(right) ?? right is null;

    public static bool operator !=(PluginStage? left, PluginStage? right)
        => !(left == right);
}
