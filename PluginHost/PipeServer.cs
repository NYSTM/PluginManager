using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PluginManager.Ipc;

namespace PluginHost;

/// <summary>
/// Named Pipe サーバーの接続受付とリクエスト送受信ループを管理します。
/// </summary>
internal sealed class PipeServer
{
    private const int MaxConcurrentClients = 4;
    private const int MaxConcurrentRequests = 8;

    private readonly string _pipeName;
    private readonly PluginRequestHandler _handler;
    private readonly PluginHostNotifier _notifier;
    private readonly SemaphoreSlim _requestSemaphore = new(MaxConcurrentRequests, MaxConcurrentRequests);
    private readonly ConcurrentBag<Task> _connectionTasks = new();

    public PipeServer(string pipeName, PluginRequestHandler handler, PluginHostNotifier notifier)
    {
        _pipeName = pipeName;
        _handler = handler;
        _notifier = notifier;
    }

    /// <summary>
    /// 最大同時接続数のサーバーインスタンスを起動し、シャットダウンまで待機します。
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _notifier.Notify(
            PluginProcessNotificationType.PipeServerStarted,
            $"Named Pipe サーバーを開始しました。Pipe={_pipeName}, MaxClients={MaxConcurrentClients}, MaxRequests={MaxConcurrentRequests}");

        var acceptTasks = new List<Task>(MaxConcurrentClients);
        for (var i = 0; i < MaxConcurrentClients; i++)
        {
            var index = i;
            acceptTasks.Add(Task.Run(() => RunServerInstanceAsync(index, cancellationToken), cancellationToken));
        }

        await Task.WhenAll(acceptTasks);
        await WaitForAllConnectionsAsync();

        _notifier.Notify(
            PluginProcessNotificationType.PipeServerStopped,
            "Named Pipe サーバーが停止しました。すべての接続処理が終了しています。");

        _requestSemaphore.Dispose();
    }

    private async Task RunServerInstanceAsync(int instanceIndex, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    MaxConcurrentClients,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                _notifier.Notify(
                    PluginProcessNotificationType.ClientConnectionWaiting,
                    $"クライアント接続を待機しています。ServerInstance={instanceIndex}");

                try
                {
                    await server.WaitForConnectionAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    await server.DisposeAsync();
                    break;
                }

                _notifier.Notify(
                    PluginProcessNotificationType.ClientConnected,
                    $"クライアント接続が確立しました。ServerInstance={instanceIndex}");

                var connectionTask = Task.Run(async () =>
                {
                    await using (server)
                    {
                        try
                        {
                            await ProcessRequestsAsync(server, instanceIndex, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _notifier.Notify(
                                PluginProcessNotificationType.ConnectionProcessingFailed,
                                $"接続処理でエラーが発生しました。ServerInstance={instanceIndex}",
                                errorType: ex.GetType().Name,
                                errorMessage: ex.Message);
                        }
                    }
                }, CancellationToken.None);

                _connectionTasks.Add(connectionTask);
            }
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _notifier.Notify(
                    PluginProcessNotificationType.ServerInstanceFailed,
                    $"サーバーインスタンスでエラーが発生しました。ServerInstance={instanceIndex}",
                    errorType: ex.GetType().Name,
                    errorMessage: ex.Message);
            }
        }
    }

    private async Task ProcessRequestsAsync(NamedPipeServerStream server, int instanceIndex, CancellationToken cancellationToken)
    {
        var buffer = new byte[65536];
        var messageBuilder = new StringBuilder();

        while (server.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var bytesRead = await server.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                    break;

                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                while (true)
                {
                    var accumulated = messageBuilder.ToString();
                    var newlineIndex = accumulated.IndexOf('\n');

                    if (newlineIndex < 0)
                        break;

                    var message = accumulated[..newlineIndex];
                    messageBuilder.Remove(0, newlineIndex + 1);

                    if (string.IsNullOrWhiteSpace(message))
                        continue;

                    var request = JsonSerializer.Deserialize<PluginHostRequest>(message);
                    if (request is null)
                        continue;

                    var handlerTask = Task.Run(async () =>
                    {
                        await _requestSemaphore.WaitAsync(cancellationToken);
                        try
                        {
                            return await _handler.HandleAsync(request, instanceIndex, cancellationToken);
                        }
                        finally
                        {
                            _requestSemaphore.Release();
                        }
                    }, cancellationToken);

                    var response = await handlerTask;
                    var responseBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response) + "\n");
                    await server.WriteAsync(responseBytes, cancellationToken);
                    await server.FlushAsync(cancellationToken);

                    if (request.Command == PluginHostCommand.Shutdown)
                        return;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _notifier.Notify(
                    PluginProcessNotificationType.RequestProcessingFailed,
                    $"要求処理でエラーが発生しました。ServerInstance={instanceIndex}",
                    errorType: ex.GetType().Name,
                    errorMessage: ex.Message);
            }
        }
    }

    private async Task WaitForAllConnectionsAsync()
    {
        while (_connectionTasks.TryTake(out var task))
        {
            try
            {
                await task;
            }
            catch
            {
                // 接続処理内で既に通知済み
            }
        }
    }
}
