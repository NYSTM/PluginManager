using System.Text.Json;
using PluginManager;

namespace PluginHost;

/// <summary>
/// プラグインへの各コマンド要求を処理します。
/// </summary>
internal sealed class PluginRequestHandler
{
    private const int DefaultExecuteTimeoutSeconds = 300;

    private readonly PluginRegistry _registry;

    public PluginRequestHandler(PluginRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// 受信した要求を対応するハンドラへルーティングします。
    /// </summary>
    public async Task<PluginHostResponse> HandleAsync(PluginHostRequest request, int instanceIndex, CancellationToken cancellationToken)
    {
        try
        {
            return request.Command switch
            {
                PluginHostCommand.Ping => new PluginHostResponse { RequestId = request.RequestId, Success = true },
                PluginHostCommand.Load => _registry.Load(request, instanceIndex),
                PluginHostCommand.Initialize => await HandleInitializeAsync(request, instanceIndex, cancellationToken),
                PluginHostCommand.Execute => await HandleExecuteAsync(request, instanceIndex, cancellationToken),
                PluginHostCommand.Unload => _registry.Unload(request, instanceIndex),
                PluginHostCommand.Shutdown => new PluginHostResponse { RequestId = request.RequestId, Success = true },
                _ => ErrorResponse(request.RequestId, $"不明なコマンド: {request.Command}", nameof(NotSupportedException)),
            };
        }
        catch (Exception ex)
        {
            return ErrorResponse(request.RequestId, ex.Message, ex.GetType().Name);
        }
    }

    private async Task<PluginHostResponse> HandleInitializeAsync(PluginHostRequest request, int instanceIndex, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.PluginId))
            return ErrorResponse(request.RequestId, "PluginId が必要です。", nameof(ArgumentException));

        if (!_registry.TryGet(request.PluginId, out var plugin))
            return ErrorResponse(request.RequestId, $"プラグイン '{request.PluginId}' がロードされていません。", nameof(InvalidOperationException));

        var context = BuildContext(request.ContextData);

        try
        {
            await plugin.InitializeAsync(context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return ErrorResponse(request.RequestId, "初期化がキャンセルされました。", nameof(OperationCanceledException));
        }

        Console.WriteLine($"[PluginHost#{instanceIndex}] 初期化完了: {request.PluginId}");
        return new PluginHostResponse
        {
            RequestId = request.RequestId,
            Success = true,
            ContextData = context.ToJsonDictionary(),
        };
    }

    private async Task<PluginHostResponse> HandleExecuteAsync(PluginHostRequest request, int instanceIndex, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.PluginId) || string.IsNullOrEmpty(request.StageId))
            return ErrorResponse(request.RequestId, "PluginId と StageId が必要です。", nameof(ArgumentException));

        if (!_registry.TryGet(request.PluginId, out var plugin))
            return ErrorResponse(request.RequestId, $"プラグイン '{request.PluginId}' がロードされていません。", nameof(InvalidOperationException));

        var stage = new PluginStage(request.StageId);
        var context = BuildContext(request.ContextData);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(DefaultExecuteTimeoutSeconds));

        try
        {
            var result = await plugin.ExecuteAsync(stage, context, timeoutCts.Token);

            Console.WriteLine($"[PluginHost#{instanceIndex}] 実行完了: {request.PluginId} @ {request.StageId}");
            return new PluginHostResponse
            {
                RequestId = request.RequestId,
                Success = true,
                ResultData = result?.ToString(),
                ContextData = context.ToJsonDictionary(),
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return ErrorResponse(request.RequestId, $"実行が {DefaultExecuteTimeoutSeconds} 秒でタイムアウトしました。", nameof(TimeoutException));
        }
        catch (OperationCanceledException)
        {
            return ErrorResponse(request.RequestId, "実行がキャンセルされました。", nameof(OperationCanceledException));
        }
        catch (Exception ex)
        {
            return ErrorResponse(request.RequestId, $"実行エラー: {ex.Message}", ex.GetType().Name);
        }
    }

    private static PluginContext BuildContext(Dictionary<string, JsonElement>? contextData)
    {
        var context = new PluginContext();
        if (contextData is not null)
            context.ApplyJsonDictionary(contextData);
        return context;
    }

    private static PluginHostResponse ErrorResponse(string requestId, string message, string errorType)
        => new() { RequestId = requestId, Success = false, ErrorMessage = message, ErrorType = errorType };
}
