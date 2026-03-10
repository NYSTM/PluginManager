namespace PluginManager;

/// <summary>
/// <see cref="PluginLoader"/> のライフサイクル通知を受け取るコールバックインターフェースです。
/// イベントハンドラの代わりに使用できる、より直感的な通知方式を提供します。
/// </summary>
/// <remarks>
/// <para>
/// <b>使い方:</b><br/>
/// このインターフェースを実装したクラスを作成し、
/// <see cref="PluginLoader.SetCallback"/> で登録してください。
/// </para>
/// <para>
/// <b>イベントとの違い:</b><br/>
/// - イベント: <c>loader.PluginEvent += (sender, e) => { ... };</c> （デリゲート構文が必要）<br/>
/// - コールバック: <c>loader.SetCallback(new MyCallback());</c> （クラスのメソッドを実装するだけ）
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class MyCallback : IPluginLoaderCallback
/// {
///     public void OnLoadStart(string configPath)
///         => Console.WriteLine($"ロード開始: {configPath}");
/// 
///     public void OnPluginLoadSuccess(string pluginId, int attempt)
///         => Console.WriteLine($"[OK] {pluginId} ({attempt} 回目)");
/// 
///     public void OnPluginLoadFailed(string pluginId, int attempt, Exception? error)
///         => Console.WriteLine($"[NG] {pluginId} - {error?.Message}");
/// }
/// 
/// var loader = new PluginLoader();
/// loader.SetCallback(new MyCallback());
/// await loader.LoadFromConfigurationAsync(...);
/// </code>
/// </example>
public interface IPluginLoaderCallback
{
    /// <summary>
    /// 設定ファイルを使用したプラグインロードが開始されたときに呼ばれます。
    /// </summary>
    /// <param name="configurationFilePath">設定ファイルのパス。</param>
    void OnLoadStart(string configurationFilePath) { }

    /// <summary>
    /// 設定ファイルを使用したプラグインロードが完了したときに呼ばれます。
    /// </summary>
    /// <param name="configurationFilePath">設定ファイルのパス。</param>
    void OnLoadCompleted(string configurationFilePath) { }

    /// <summary>
    /// 個別プラグインのロードが開始されたときに呼ばれます。
    /// </summary>
    /// <param name="pluginId">プラグインの ID。</param>
    /// <param name="attempt">試行回数（1 から始まる）。</param>
    void OnPluginLoadStart(string pluginId, int attempt) { }

    /// <summary>
    /// 個別プラグインのロードをリトライするときに呼ばれます。
    /// </summary>
    /// <param name="pluginId">プラグインの ID。</param>
    /// <param name="attempt">試行回数（1 から始まる）。</param>
    /// <param name="error">前回の失敗理由。</param>
    void OnPluginLoadRetry(string pluginId, int attempt, Exception? error) { }

    /// <summary>
    /// 個別プラグインのロードに成功したときに呼ばれます。
    /// </summary>
    /// <param name="pluginId">プラグインの ID。</param>
    /// <param name="attempt">試行回数（1 から始まる）。</param>
    void OnPluginLoadSuccess(string pluginId, int attempt) { }

    /// <summary>
    /// 個別プラグインのロードに失敗したときに呼ばれます。
    /// リトライ上限到達・恒久的エラー・キャンセルを含みます。
    /// </summary>
    /// <param name="pluginId">プラグインの ID。</param>
    /// <param name="attempt">試行回数（1 から始まる）。</param>
    /// <param name="error">失敗理由。</param>
    void OnPluginLoadFailed(string pluginId, int attempt, Exception? error) { }

    /// <summary>
    /// ステージ実行が開始されたときに呼ばれます。
    /// </summary>
    /// <param name="stageId">ステージの ID。</param>
    void OnExecuteStart(string stageId) { }

    /// <summary>
    /// ステージ実行が完了したときに呼ばれます。
    /// </summary>
    /// <param name="stageId">ステージの ID。</param>
    void OnExecuteCompleted(string stageId) { }

    /// <summary>
    /// ステージ実行中にエラーが発生したときに呼ばれます。
    /// </summary>
    /// <param name="stageId">ステージの ID。</param>
    /// <param name="error">エラー内容。</param>
    void OnExecuteFailed(string stageId, Exception error) { }
}
