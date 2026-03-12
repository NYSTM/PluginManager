using System.Collections.Frozen;

namespace PluginManager;

/// <summary>
/// プラグイン実装の基底クラスです。
/// </summary>
/// <remarks>
/// <para>
/// このクラスは、<see cref="IPlugin"/> の実装を簡素化するための
/// 便利な基底クラスです。
/// </para>
/// <para>
/// <b>提供される機能:</b>
/// <list type="bullet">
/// <item>メタデータの自動実装（コンストラクタで設定）</item>
/// <item>初期化のデフォルト実装（オーバーライド可能）</item>
/// <item>ステージ実行の委譲（派生クラスで OnExecuteAsync を実装）</item>
/// <item>リソース管理のサポート（<see cref="IAsyncDisposable"/>）</item>
/// </list>
/// </para>
/// <para>
/// <b>使用するべきケース:</b>
/// <list type="bullet">
/// <item>シンプルなプラグインを素早く実装したい</item>
/// <item>メタデータのボイラープレートを減らしたい</item>
/// <item>初期化が不要、または軽量な場合</item>
/// </list>
/// </para>
/// <para>
/// <b>使用しないべきケース:</b>
/// <list type="bullet">
/// <item>複雑な初期化ロジックが必要</item>
/// <item>メタデータを動的に変更する必要がある</item>
/// <item>完全なカスタム実装が必要</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 最小実装
/// public sealed class SimplePlugin : PluginBase
/// {
///     public SimplePlugin()
///         : base("simple-plugin", "シンプルプラグイン", new Version(1, 0, 0), PluginStage.Processing)
///     {
///     }
///     
///     protected override async Task&lt;object?&gt; OnExecuteAsync(
///         PluginStage stage,
///         PluginContext context,
///         CancellationToken cancellationToken)
///     {
///         // 実行処理のみ実装
///         await Task.Delay(100, cancellationToken);
///         return "完了";
///     }
/// }
/// 
/// // 初期化をカスタマイズ
/// public sealed class CustomInitPlugin : PluginBase
/// {
///     public CustomInitPlugin()
///         : base("custom-init", "カスタム初期化", new Version(1, 0, 0), PluginStage.Processing)
///     {
///     }
///     
///     protected override async Task OnInitializeAsync(
///         PluginContext context,
///         CancellationToken cancellationToken)
///     {
///         // 初期化処理をカスタマイズ
///         await ConnectToDatabaseAsync(cancellationToken);
///     }
///     
///     protected override async Task&lt;object?&gt; OnExecuteAsync(
///         PluginStage stage,
///         PluginContext context,
///         CancellationToken cancellationToken)
///     {
///         return "完了";
///     }
/// }
/// 
/// // リソース管理
/// public sealed class ResourcePlugin : PluginBase
/// {
///     private HttpClient? _client;
///     
///     public ResourcePlugin()
///         : base("resource", "リソース管理", new Version(1, 0, 0), PluginStage.Processing)
///     {
///     }
///     
///     protected override async Task OnInitializeAsync(
///         PluginContext context,
///         CancellationToken cancellationToken)
///     {
///         _client = new HttpClient();
///         await Task.CompletedTask;
///     }
///     
///     protected override async Task&lt;object?&gt; OnExecuteAsync(
///         PluginStage stage,
///         PluginContext context,
///         CancellationToken cancellationToken)
///     {
///         var response = await _client!.GetStringAsync("https://example.com", cancellationToken);
///         return response;
///     }
///     
///     protected override async ValueTask DisposeAsyncCore()
///     {
///         _client?.Dispose();
///         await base.DisposeAsyncCore();
///     }
/// }
/// </code>
/// </example>
public abstract class PluginBase : IPlugin, IAsyncDisposable
{
    private bool _disposed;

    /// <summary>
    /// プラグインの基本情報を指定してインスタンスを初期化します。
    /// </summary>
    /// <param name="id">プラグインを一意に識別するID。</param>
    /// <param name="name">プラグインの表示名。</param>
    /// <param name="version">プラグインのバージョン。</param>
    /// <param name="supportedStages">プラグインが実行対象とするライフサイクルステージ一覧。</param>
    protected PluginBase(string id, string name, Version version, params PluginStage[] supportedStages)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("プラグインIDは必須です。", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("プラグイン名は必須です。", nameof(name));

        Id = id;
        Name = name;
        Version = version ?? throw new ArgumentNullException(nameof(version));
        SupportedStages = supportedStages?.ToFrozenSet() ?? FrozenSet<PluginStage>.Empty;

        if (SupportedStages.Count == 0)
            throw new ArgumentException("少なくとも1つのステージを指定してください。", nameof(supportedStages));
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public Version Version { get; }

    /// <inheritdoc/>
    public IReadOnlySet<PluginStage> SupportedStages { get; }

    /// <inheritdoc/>
    public async Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await OnInitializeAsync(context, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await OnExecuteAsync(stage, context, cancellationToken);
    }

    /// <summary>
    /// プラグインの初期化処理を実行します。
    /// </summary>
    /// <param name="context">初期化に利用する実行コンテキスト。</param>
    /// <param name="cancellationToken">初期化処理のキャンセル通知。</param>
    /// <returns>初期化処理を表す <see cref="Task"/>。</returns>
    /// <remarks>
    /// <para>
    /// このメソッドは、派生クラスでオーバーライドして初期化ロジックを実装します。
    /// </para>
    /// <para>
    /// <b>デフォルト実装:</b> 何もしません（<see cref="Task.CompletedTask"/> を返す）。
    /// </para>
    /// </remarks>
    protected virtual Task OnInitializeAsync(PluginContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// プラグインの実行処理を実行します。
    /// </summary>
    /// <param name="stage">実行するライフサイクルステージ。</param>
    /// <param name="context">実行コンテキスト。</param>
    /// <param name="cancellationToken">キャンセル通知。</param>
    /// <returns>プラグインの実行結果。結果がない場合は <see langword="null"/>。</returns>
    /// <remarks>
    /// <para>
    /// このメソッドは、派生クラスで必ずオーバーライドして実行ロジックを実装してください。
    /// </para>
    /// </remarks>
    protected abstract Task<object?> OnExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
        _disposed = true;
    }

    /// <summary>
    /// リソースの非同期解放処理を実行します。
    /// </summary>
    /// <returns>解放処理を表す <see cref="ValueTask"/>。</returns>
    /// <remarks>
    /// <para>
    /// 派生クラスでリソース解放が必要な場合は、このメソッドをオーバーライドしてください。
    /// </para>
    /// <para>
    /// <b>デフォルト実装:</b> 何もしません（<see cref="ValueTask.CompletedTask"/> を返す）。
    /// </para>
    /// </remarks>
    protected virtual ValueTask DisposeAsyncCore()
        => ValueTask.CompletedTask;
}
