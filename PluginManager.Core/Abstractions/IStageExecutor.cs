namespace PluginManager;

/// <summary>
/// プラグインのステージ実行を担当するインターフェースです。
/// </summary>
/// <remarks>
/// <para>
/// このインターフェースは、プラグインのメイン処理を定義します。
/// </para>
/// <para>
/// <b>実行の責務:</b>
/// <list type="bullet">
/// <item>ステージに応じた処理の実行</item>
/// <item>コンテキストを介したデータの読み書き</item>
/// <item>処理結果の返却</item>
/// <item>エラーハンドリング</item>
/// </list>
/// </para>
/// <para>
/// <b>実装のベストプラクティス:</b>
/// <list type="number">
/// <item>ステートレスに実装する（状態は <see cref="PluginContext"/> で管理）</item>
/// <item>冪等性を保つ（同じ入力で何度実行しても同じ結果）</item>
/// <item>副作用を最小限にする</item>
/// <item>CancellationToken を尊重する</item>
/// <item>長時間実行する場合は進捗を報告する</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class MyPlugin : IStageExecutor
/// {
///     public async Task&lt;object?&gt; ExecuteAsync(
///         PluginStage stage,
///         PluginContext context,
///         CancellationToken cancellationToken)
///     {
///         // 入力データ取得
///         var inputData = context.GetProperty&lt;string&gt;("InputData");
///         
///         // 処理実行
///         var result = await ProcessDataAsync(inputData, cancellationToken);
///         
///         // 結果を設定
///         context.SetProperty("ProcessedData", result);
///         
///         return result;
///     }
/// }
/// </code>
/// </example>
public interface IStageExecutor
{
    /// <summary>
    /// 指定ステージでプラグインを非同期実行し、結果を返します。
    /// </summary>
    /// <param name="stage">実行するライフサイクルステージ。</param>
    /// <param name="context">実行コンテキスト。ステージ間でデータを共有します。</param>
    /// <param name="cancellationToken">キャンセル通知。</param>
    /// <returns>プラグインの実行結果。結果がない場合は <see langword="null"/>。</returns>
    /// <remarks>
    /// <para>
    /// このメソッドは、<see cref="IPluginMetadata.SupportedStages"/> に含まれる
    /// ステージでのみ呼ばれます。フレームワークが自動的にフィルタリングします。
    /// </para>
    /// <para>
    /// <b>戻り値:</b><br/>
    /// 処理結果を返すことができます。結果は <see cref="PluginExecutionResult.Value"/>
    /// に格納されます。結果がない場合は <see langword="null"/> を返してください。
    /// </para>
    /// <para>
    /// <b>コンテキストの使用:</b><br/>
    /// <see cref="PluginContext"/> を使用して、前のステージの結果を取得したり、
    /// 後続ステージへデータを渡したりできます。
    /// </para>
    /// <para>
    /// <b>エラーハンドリング:</b><br/>
    /// 実行失敗時は例外をスローしてください。例外は自動的に
    /// <see cref="PluginErrorCategory.ExecutionFailure"/> として分類されます。
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">実行に失敗した場合。</exception>
    /// <exception cref="OperationCanceledException">キャンセルされた場合。</exception>
    Task<object?> ExecuteAsync(
        PluginStage stage,
        PluginContext context,
        CancellationToken cancellationToken = default);
}
