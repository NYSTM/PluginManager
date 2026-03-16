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
    private const int HostProcessExitWaitMilliseconds = 5000;
    private const int MaxUnexpectedReadCancellationRetries = 2;

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

            var requestJson = JsonSerializer.Serialize(request) + "\n";
            var requestBytes = Encoding.UTF8.GetBytes(requestJson);

            try
            {
                await client.WriteAsync(requestBytes, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IOException("ホストプロセスへの要求送信中に接続が切断されました。");
            }

            var buffer = new byte[65536];
            var messageBuilder = new StringBuilder();
            var unexpectedReadCancellationCount = 0;

            while (true)
            {
                int bytesRead;
                try
                {
                    bytesRead = await client.ReadAsync(buffer, cancellationToken);
                    unexpectedReadCancellationCount = 0;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    if (unexpectedReadCancellationCount < MaxUnexpectedReadCancellationRetries && IsClientConnected(client))
                    {
                        unexpectedReadCancellationCount++;
                        continue;
                    }

                    throw new IOException("ホストプロセスからの応答受信中に接続が切断されました。");
                }

                if (bytesRead == 0)
                    throw new IOException("ホストプロセスが切断されました。");

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

                    var response = JsonSerializer.Deserialize<PluginHostResponse>(message);
                    if (response is null)
                        throw new InvalidOperationException("ホストプロセスからの応答が不正です。");

                    if (response.RequestId != request.RequestId)
                        continue;

                    return response;
                }
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static bool IsClientConnected(NamedPipeClientStream client)
    {
        try
        {
            return client.IsConnected;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
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
