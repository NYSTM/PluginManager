using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace PluginManager.Ipc;

/// <summary>
/// PluginHost プロセスと通信する Named Pipe クライアントです。
/// </summary>
internal sealed class PluginHostClient : IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private const int HostProcessExitWaitMilliseconds = 5000;
    private const int MaxRequestIdMismatchCount = 10;
    private const int MaxFramePayloadBytes = 1024 * 1024;

    private readonly string _pipeName;
    private readonly Process? _hostProcess;
    private NamedPipeClientStream? _client;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _disposed;

    public PluginHostClient(string pipeName, Process? hostProcess = null)
    {
        _pipeName = pipeName;
        _hostProcess = hostProcess;
    }

    public async Task ConnectAsync(int timeoutMilliseconds = 5000, CancellationToken cancellationToken = default)
    {
        _client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMilliseconds);

        try
        {
            await _client.ConnectAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"ホストプロセスへの接続がタイムアウトしました。Pipe={_pipeName}");
        }
    }

    public async Task<PluginHostResponse> SendRequestAsync(PluginHostRequest request, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) == 1)
            throw new ObjectDisposedException(nameof(PluginHostClient));

        if (_client is null || !_client.IsConnected)
            throw new InvalidOperationException("ホストプロセスに接続されていません。");

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _disposed) == 1)
                throw new ObjectDisposedException(nameof(PluginHostClient));

            var client = _client;
            if (client is null || !client.IsConnected)
                throw new InvalidOperationException("ホストプロセスに接続されていません。");

            var requestJson = JsonSerializer.Serialize(request);
            var requestBytes = StrictUtf8.GetBytes(requestJson);
            await WriteFrameAsync(client, requestBytes, cancellationToken);

            var mismatchCount = 0;

            while (true)
            {
                var responseBytes = await ReadFrameAsync(client, cancellationToken);
                var responseJson = StrictUtf8.GetString(responseBytes);

                var response = JsonSerializer.Deserialize<PluginHostResponse>(responseJson);
                if (response is null)
                    throw new InvalidOperationException("ホストプロセスからの応答が不正です。");

                if (response.RequestId != request.RequestId)
                {
                    if (++mismatchCount > MaxRequestIdMismatchCount)
                    {
                        InvalidateConnection(client);
                        throw new IOException($"ホストプロセスからの応答リクエストIDが一致しません。(期待値: {request.RequestId}, 実際値: {response.RequestId})");
                    }

                    continue;
                }

                return response;
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task WriteFrameAsync(NamedPipeClientStream client, byte[] payload, CancellationToken cancellationToken)
    {
        if (payload.Length > MaxFramePayloadBytes)
            throw new InvalidOperationException($"送信フレームサイズが上限を超えています。Size={payload.Length}");

        var frame = new byte[sizeof(int) + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, sizeof(int)), payload.Length);
        payload.CopyTo(frame.AsSpan(sizeof(int)));

        try
        {
            await client.WriteAsync(frame, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            InvalidateConnection(client);
            throw new IOException("ホストプロセスへの要求送信中に接続が切断されました。");
        }
    }

    private async Task<byte[]> ReadFrameAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        var lengthPrefix = new byte[4];
        await ReadExactlyAsync(client, lengthPrefix, cancellationToken, "ホストプロセスからの応答受信中に接続が切断されました。");

        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);
        if (payloadLength <= 0 || payloadLength > MaxFramePayloadBytes)
        {
            InvalidateConnection(client);
            throw new InvalidOperationException($"ホストプロセスからの応答フレームサイズが不正です。Size={payloadLength}");
        }

        var payload = new byte[payloadLength];
        await ReadExactlyAsync(client, payload, cancellationToken, "ホストプロセスからの応答受信中に接続が切断されました。");

        return payload;
    }

    private async Task ReadExactlyAsync(NamedPipeClientStream client, byte[] buffer, CancellationToken cancellationToken, string disconnectionMessage)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            int bytesRead;
            try
            {
                bytesRead = await client.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                InvalidateConnection(client);
                throw new IOException(disconnectionMessage);
            }

            if (bytesRead == 0)
            {
                InvalidateConnection(client);
                throw new IOException("ホストプロセスが切断されました。");
            }

            offset += bytesRead;
        }
    }

    private void InvalidateConnection(NamedPipeClientStream client)
    {
        try
        {
            client.Dispose();
        }
        catch
        {
            // 接続破棄失敗は無視
        }

        if (ReferenceEquals(_client, client))
            _client = null;
    }

    public bool IsConnected => _client?.IsConnected ?? false;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _client?.Dispose();
        _sendLock.Dispose();

        if (_hostProcess is not null)
        {
            var processId = TryGetProcessId(_hostProcess);

            try
            {
                EnsureHostProcessExited(_hostProcess);
            }
            catch
            {
                // プロセス終了失敗は無視
            }
            finally
            {
                _hostProcess.Dispose();
            }

            if (processId is int id)
            {
                try
                {
                    EnsureHostProcessExited(id);
                }
                catch
                {
                    // プロセス終了失敗は無視
                }
            }
        }
    }

    private static int? TryGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureHostProcessExited(Process process)
    {
        if (process.HasExited)
            return;

        process.Kill(entireProcessTree: true);
        process.WaitForExit(HostProcessExitWaitMilliseconds);
    }

    private static void EnsureHostProcessExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
                return;

            process.Kill(entireProcessTree: true);
            process.WaitForExit(HostProcessExitWaitMilliseconds);
        }
        catch (ArgumentException)
        {
            // 既に終了済み
        }
    }
}
