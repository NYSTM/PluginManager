namespace PluginManager;

/// <summary>
/// プラグインエラーの分類を表します。
/// </summary>
/// <remarks>
/// <para>
/// エラーカテゴリは、リトライ判定や原因分析に使用されます。
/// </para>
/// <list type="bullet">
/// <item><term>InitializationFailure</term><description>初期化失敗（設定読み込み・DB 接続など）</description></item>
/// <item><term>ExecutionFailure</term><description>実行失敗（処理ロジックのエラー）</description></item>
/// <item><term>TransientFailure</term><description>一時的障害（ネットワークタイムアウト・リソース不足）</description></item>
/// <item><term>PermanentFailure</term><description>永続的障害（設定不備・API キー不正）</description></item>
/// <item><term>ContractViolation</term><description>契約違反（IPlugin 未実装・必須メソッド不備）</description></item>
/// <item><term>DependencyFailure</term><description>依存関係不足（依存プラグイン未ロード・ライブラリ不足）</description></item>
/// <item><term>Timeout</term><description>タイムアウト（処理時間超過）</description></item>
/// <item><term>Cancellation</term><description>キャンセル（CancellationToken による中断）</description></item>
/// <item><term>Unknown</term><description>不明（分類不能なエラー）</description></item>
/// </list>
/// </remarks>
public enum PluginErrorCategory
{
    /// <summary>
    /// 不明なエラー。分類できない例外やカテゴリが指定されていない場合。
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 初期化失敗。
    /// <see cref="IPlugin.InitializeAsync"/> で発生したエラー。
    /// 設定ファイル読み込み失敗・DB 接続失敗などが該当します。
    /// </summary>
    /// <remarks>
    /// <b>リトライ推奨:</b> 一時的な接続エラーの場合はリトライ可能。
    /// </remarks>
    InitializationFailure,

    /// <summary>
    /// 実行失敗。
    /// <see cref="IPlugin.ExecuteAsync"/> で発生したエラー。
    /// 処理ロジック内のバグ・データ検証失敗などが該当します。
    /// </summary>
    /// <remarks>
    /// <b>リトライ推奨:</b> ロジックエラーの場合はリトライ不要。
    /// </remarks>
    ExecutionFailure,

    /// <summary>
    /// 一時的障害。
    /// ネットワークタイムアウト・リソース一時不足・ALC 競合などが該当します。
    /// </summary>
    /// <remarks>
    /// <b>リトライ推奨:</b> 時間を置いて再試行すると回復する可能性が高い。
    /// </remarks>
    TransientFailure,

    /// <summary>
    /// 永続的障害。
    /// 設定ファイル不備・API キー不正・必須ファイル欠落などが該当します。
    /// </summary>
    /// <remarks>
    /// <b>リトライ推奨:</b> 設定修正が必要なため、リトライしても回復しない。
    /// </remarks>
    PermanentFailure,

    /// <summary>
    /// 契約違反。
    /// <see cref="IPlugin"/> 未実装・必須メソッド不備・型不一致などが該当します。
    /// </summary>
    /// <remarks>
    /// <b>リトライ推奨:</b> プラグイン実装の修正が必要なため、リトライ不要。
    /// </remarks>
    ContractViolation,

    /// <summary>
    /// 依存関係不足。
    /// 依存プラグイン未ロード・必須ライブラリ欠落・循環依存などが該当します。
    /// </summary>
    /// <remarks>
    /// <b>リトライ推奨:</b> 依存関係を解決してから再試行。
    /// </remarks>
    DependencyFailure,

    /// <summary>
    /// タイムアウト。
    /// 処理時間が設定値を超過した場合。
    /// </summary>
    /// <remarks>
    /// <b>リトライ推奨:</b> 負荷が下がればリトライ可能。
    /// </remarks>
    Timeout,

    /// <summary>
    /// キャンセル。
    /// <see cref="System.Threading.CancellationToken"/> による中断。
    /// </summary>
    /// <remarks>
    /// <b>リトライ推奨:</b> ユーザー操作によるキャンセルのため、リトライ不要。
    /// </remarks>
    Cancellation,
}
