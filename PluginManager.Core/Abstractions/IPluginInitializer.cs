namespace PluginManager;

/// <summary>
/// プラグインの初期化を担当するインターフェースです。
/// </summary>
/// <remarks>
/// <para>
/// このインターフェースは、プラグインのライフサイクル開始時に呼ばれる
/// 初期化処理を定義します。
/// </para>
/// <para>
/// <b>初期化の責務:</b>
/// <list type="bullet">
/// <item>データベース接続の確立</item>
/// <item>設定ファイルの読み込み</item>
/// <item>リソースの割り当て</item>
/// <item>依存サービスの接続確認</item>
/// </list>
/// </para>
/// <para>
/// <b>実装のベストプラクティス:</b>
/// <list type="number">
/// <item>初期化は冪等に実装する（複数回呼ばれても安全）</item>
/// <item>重い処理は遅延初期化を検討する</item>
/// <item>失敗時は適切な例外をスローする</item>
/// <item>CancellationToken を尊重する</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class MyPlugin : IPluginInitializer
/// {
///     private HttpClient? _client;
///     
///     public async Task InitializeAsync(PluginContext context, CancellationToken cancellationToken)
///     {
///         // DB 接続
///         var connectionString = context.GetProperty&lt;string&gt;("ConnectionString");
///         await ConnectToDatabaseAsync(connectionString, cancellationToken);
///         
///         // HTTP クライアント初期化
///         _client = new HttpClient();
///         
///         // 設定検証
///         if (!ValidateConfiguration())
///             throw new InvalidOperationException("設定が不正です");
///     }
/// }
/// </code>
/// </example>
public interface IPluginInitializer
{
    /// <summary>
    /// プラグインを初期化します。
    /// </summary>
    /// <param name="context">初期化に利用する実行コンテキスト。</param>
    /// <param name="cancellationToken">初期化処理のキャンセル通知。</param>
    /// <returns>初期化処理を表す <see cref="Task"/>。</returns>
    /// <remarks>
    /// <para>
    /// このメソッドは、プラグインのロード直後に1回だけ呼ばれます。
    /// </para>
    /// <para>
    /// <b>タイムアウト:</b><br/>
    /// pluginsettings.json の <c>TimeoutMilliseconds</c> が適用されます。
    /// </para>
    /// <para>
    /// <b>リトライ:</b><br/>
    /// 初期化失敗時は <see cref="PluginErrorCategory.InitializationFailure"/> として
    /// 分類され、一時的エラーの場合は自動的にリトライされます。
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">初期化に失敗した場合。</exception>
    /// <exception cref="OperationCanceledException">キャンセルされた場合。</exception>
    Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default);
}
