using System.Text.Json;
using PluginManager;
using PluginManager.Ipc;

namespace PluginHost;

/// <summary>
/// プラグインへの各コマンド要求を処理します。
/// </summary>
internal sealed class PluginRequestHandler
{
    private const int DefaultExecuteTimeoutSeconds = 300;

    private readonly PluginRegistry _registry;
    private readonly PluginHostNotifier _notifier;

    public PluginRequestHandler(PluginRegistry registry, PluginHostNotifier notifier)
    {
        _registry = registry;
        _notifier = notifier;
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
                PluginHostCommand.Load => HandleLoad(request, instanceIndex),
                PluginHostCommand.Initialize => await HandleInitializeAsync(request, instanceIndex, cancellationToken),
                PluginHostCommand.Execute => await HandleExecuteAsync(request, instanceIndex, cancellationToken),
                PluginHostCommand.Unload => HandleUnload(request, instanceIndex),
                PluginHostCommand.Shutdown => HandleShutdown(request),
                _ => ErrorResponse(request.RequestId, $"不明なコマンド: {request.Command}", nameof(NotSupportedException)),
            };
        }
        catch (Exception ex)
        {
            PublishNotification(
                PluginProcessNotificationType.RequestProcessingFailed,
                "要求処理中に未処理例外が発生しました。",
                request,
                ex.GetType().Name,
                ex.Message);
            return ErrorResponse(request.RequestId, ex.Message, ex.GetType().Name);
        }
    }

    private PluginHostResponse HandleLoad(PluginHostRequest request, int instanceIndex)
    {
        var response = _registry.Load(request, instanceIndex);
        PublishNotification(
            response.Success ? PluginProcessNotificationType.LoadCompleted : PluginProcessNotificationType.LoadFailed,
            response.Success
                ? $"プラグイン '{request.PluginId}' のロードが完了しました。"
                : $"プラグイン '{request.PluginId}' のロードに失敗しました。",
            request,
            response.ErrorType,
            response.ErrorMessage);
        return response;
    }

    private PluginHostResponse HandleUnload(PluginHostRequest request, int instanceIndex)
    {
        var response = _registry.Unload(request, instanceIndex);
        PublishNotification(
            response.Success ? PluginProcessNotificationType.UnloadCompleted : PluginProcessNotificationType.UnloadFailed,
            response.Success
                ? $"プラグイン '{request.PluginId}' のアンロードが完了しました。"
                : $"プラグイン '{request.PluginId}' のアンロードに失敗しました。",
            request,
            response.ErrorType,
            response.ErrorMessage);
        return response;
    }

    private PluginHostResponse HandleShutdown(PluginHostRequest request)
    {
        PublishNotification(
            PluginProcessNotificationType.ShutdownReceived,
            "PluginHost がシャットダウンコマンドを受信しました。",
            request);
        return new PluginHostResponse { RequestId = request.RequestId, Success = true };
    }

    private async Task<PluginHostResponse> HandleInitializeAsync(PluginHostRequest request, int instanceIndex, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.PluginId))
            return ErrorResponse(request.RequestId, "PluginId が必要です。", nameof(ArgumentException));

        if (!_registry.TryGet(request.PluginId, out var plugin))
            return ErrorResponse(request.RequestId, $"プラグイン '{request.PluginId}' がロードされていません。", nameof(InvalidOperationException));

        var context = BuildContext(request.ContextData);
        PublishNotification(
            PluginProcessNotificationType.InitializeStarted,
            $"プラグイン '{request.PluginId}' の初期化を開始します。",
            request);

        try
        {
            await plugin.InitializeAsync(context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            PublishNotification(
                PluginProcessNotificationType.InitializeFailed,
                $"プラグイン '{request.PluginId}' の初期化がキャンセルされました。",
                request,
                nameof(OperationCanceledException),
                "初期化がキャンセルされました。");
            return ErrorResponse(request.RequestId, "初期化がキャンセルされました。", nameof(OperationCanceledException));
        }
        catch (Exception ex)
        {
            PublishNotification(
                PluginProcessNotificationType.InitializeFailed,
                $"プラグイン '{request.PluginId}' の初期化に失敗しました。",
                request,
                ex.GetType().Name,
                ex.Message);
            return ErrorResponse(request.RequestId, ex.Message, ex.GetType().Name);
        }

        PublishNotification(
            PluginProcessNotificationType.InitializeCompleted,
            $"プラグイン '{request.PluginId}' の初期化が完了しました。",
            request);
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
        PublishNotification(
            PluginProcessNotificationType.ExecuteStarted,
            $"プラグイン '{request.PluginId}' の実行を開始します。",
            request);

        try
        {
            var result = await plugin.ExecuteAsync(stage, context, timeoutCts.Token);

            PublishNotification(
                PluginProcessNotificationType.ExecuteCompleted,
                $"プラグイン '{request.PluginId}' の実行が完了しました。",
                request);
            return new PluginHostResponse
            {
                RequestId = request.RequestId,
                Success = true,
                ResultData = result is not null ? JsonSerializer.SerializeToElement(result) : null,
                ContextData = context.ToJsonDictionary(),
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            PublishNotification(
                PluginProcessNotificationType.ExecuteFailed,
                $"プラグイン '{request.PluginId}' の実行がタイムアウトしました。",
                request,
                nameof(TimeoutException),
                $"実行が {DefaultExecuteTimeoutSeconds} 秒でタイムアウトしました。");
            return ErrorResponse(request.RequestId, $"実行が {DefaultExecuteTimeoutSeconds} 秒でタイムアウトしました。", nameof(TimeoutException));
        }
        catch (OperationCanceledException)
        {
            PublishNotification(
                PluginProcessNotificationType.ExecuteFailed,
                $"プラグイン '{request.PluginId}' の実行がキャンセルされました。",
                request,
                nameof(OperationCanceledException),
                "実行がキャンセルされました。");
            return ErrorResponse(request.RequestId, "実行がキャンセルされました。", nameof(OperationCanceledException));
        }
        catch (Exception ex)
        {
            PublishNotification(
                PluginProcessNotificationType.ExecuteFailed,
                $"プラグイン '{request.PluginId}' の実行に失敗しました。",
                request,
                ex.GetType().Name,
                ex.Message);
            return ErrorResponse(request.RequestId, $"実行エラー: {ex.Message}", ex.GetType().Name);
        }
    }

    private void PublishNotification(
        PluginProcessNotificationType notificationType,
        string message,
        PluginHostRequest request,
        string? errorType = null,
        string? errorMessage = null)
        => _notifier.Notify(
            notificationType,
            message,
            requestId: request.RequestId,
            pluginId: request.PluginId,
            stageId: request.StageId,
            errorType: errorType,
            errorMessage: errorMessage);

    private static PluginContext BuildContext(IReadOnlyDictionary<string, JsonElement>? contextData)
    {
        var context = new PluginContext();
        if (contextData is not null)
            context.ApplyJsonDictionary(contextData);
        return context;
    }

    private static PluginHostResponse ErrorResponse(string requestId, string message, string errorType)
        => new() { RequestId = requestId, Success = false, ErrorMessage = message, ErrorType = errorType };
}
