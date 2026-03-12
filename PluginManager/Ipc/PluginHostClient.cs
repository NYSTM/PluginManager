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
            // ロック取得後に再チェック
            if (Volatile.Read(ref _disposed) == 1)
                throw new ObjectDisposedException(nameof(PluginHostClient));

            var requestJson = JsonSerializer.Serialize(request) + "\n";
            var requestBytes = Encoding.UTF8.GetBytes(requestJson);
            await _client.WriteAsync(requestBytes, cancellationToken);
            await _client.FlushAsync(cancellationToken);

            var buffer = new byte[65536];
            var messageBuilder = new StringBuilder();

            while (true)
            {
                var bytesRead = await _client.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                    throw new IOException("ホストプロセスが切断されました。");

                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                var message = messageBuilder.ToString();
                if (!message.EndsWith('\n'))
                    continue;

                var response = JsonSerializer.Deserialize<PluginHostResponse>(message.TrimEnd('\n'));
                if (response is null)
                    throw new InvalidOperationException("ホストプロセスからの応答が不正です。");

                if (response.RequestId != request.RequestId)
                    throw new InvalidOperationException($"応答の RequestId が一致しません。Expected={request.RequestId}, Actual={response.RequestId}");

                return response;
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public bool IsConnected => _client?.IsConnected ?? false;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _client?.Dispose();
        _sendLock.Dispose();

        if (_hostProcess is not null && !_hostProcess.HasExited)
        {
            try
            {
                _hostProcess.Kill();
                _hostProcess.WaitForExit(1000);
            }
            catch
            {
                // プロセス終了失敗は無視
            }
        }

        _hostProcess?.Dispose();
    }
}
