using System.Buffers.Binary;
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
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private const int MaxConcurrentClients = 4;
    private const int MaxConcurrentRequests = 8;
    private const int MaxFramePayloadBytes = 1024 * 1024;

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
            acceptTasks.Add(Task.Run(() => RunServerInstanceAsync(index, cancellationToken), CancellationToken.None));
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
        while (server.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var requestBytes = await ReadFrameAsync(server, cancellationToken);
                var requestJson = StrictUtf8.GetString(requestBytes);
                var request = JsonSerializer.Deserialize<PluginHostRequest>(requestJson);
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
                var responseBytes = StrictUtf8.GetBytes(JsonSerializer.Serialize(response));
                await WriteFrameAsync(server, responseBytes, cancellationToken);

                if (request.Command == PluginHostCommand.Shutdown)
                    return;
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

    private static async Task WriteFrameAsync(NamedPipeServerStream server, byte[] payload, CancellationToken cancellationToken)
    {
        if (payload.Length > MaxFramePayloadBytes)
            throw new InvalidOperationException($"応答フレームサイズが上限を超えています。Size={payload.Length}");

        var frame = new byte[sizeof(int) + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, sizeof(int)), payload.Length);
        payload.CopyTo(frame.AsSpan(sizeof(int)));

        await server.WriteAsync(frame, cancellationToken);
        await server.FlushAsync(cancellationToken);
    }

    private static async Task<byte[]> ReadFrameAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        var lengthPrefix = new byte[4];
        await ReadExactlyAsync(server, lengthPrefix, cancellationToken);

        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);
        if (payloadLength <= 0 || payloadLength > MaxFramePayloadBytes)
            throw new InvalidOperationException($"要求フレームサイズが不正です。Size={payloadLength}");

        var payload = new byte[payloadLength];
        await ReadExactlyAsync(server, payload, cancellationToken);
        return payload;
    }

    private static async Task ReadExactlyAsync(NamedPipeServerStream server, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var bytesRead = await server.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (bytesRead == 0)
                throw new IOException("クライアント接続が切断されました。");

            offset += bytesRead;
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
