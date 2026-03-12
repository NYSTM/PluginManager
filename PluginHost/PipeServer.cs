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
    private readonly SemaphoreSlim _requestSemaphore = new(MaxConcurrentRequests, MaxConcurrentRequests);
    private readonly ConcurrentBag<Task> _connectionTasks = new();

    public PipeServer(string pipeName, PluginRequestHandler handler)
    {
        _pipeName = pipeName;
        _handler = handler;
    }

    /// <summary>
    /// 最大同時接続数のサーバーインスタンスを起動し、シャットダウンまで待機します。
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"[PluginHost] 最大同時接続数: {MaxConcurrentClients}, 最大並列リクエスト数: {MaxConcurrentRequests}");

        var acceptTasks = new List<Task>(MaxConcurrentClients);
        for (var i = 0; i < MaxConcurrentClients; i++)
        {
            var index = i;
            acceptTasks.Add(Task.Run(() => RunServerInstanceAsync(index, cancellationToken), cancellationToken));
        }

        await Task.WhenAll(acceptTasks);
        Console.WriteLine("[PluginHost] すべての受付タスクが終了しました。接続処理の完了を待機中...");

        await WaitForAllConnectionsAsync();
        Console.WriteLine("[PluginHost] すべての接続処理が終了しました。");

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

                Console.WriteLine($"[PluginHost#{instanceIndex}] クライアント接続待機中...");

                try
                {
                    await server.WaitForConnectionAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    await server.DisposeAsync();
                    break;
                }

                Console.WriteLine($"[PluginHost#{instanceIndex}] クライアント接続完了");

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
                            Console.Error.WriteLine($"[PluginHost#{instanceIndex}] 接続処理エラー: {ex.Message}");
                        }
                    }
                }, CancellationToken.None);

                _connectionTasks.Add(connectionTask);
            }
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                Console.Error.WriteLine($"[PluginHost#{instanceIndex}] サーバーインスタンスエラー: {ex.Message}");
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
                    {
                        Console.WriteLine($"[PluginHost#{instanceIndex}] シャットダウンコマンドを受信");
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PluginHost#{instanceIndex}] 要求処理エラー: {ex.Message}");
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
                // 接続処理内で既にログ出力済み
            }
        }
    }
}
