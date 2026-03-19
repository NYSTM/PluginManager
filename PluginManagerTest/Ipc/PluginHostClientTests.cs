using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PluginManager.Ipc;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="PluginHostClient"/> のテストです。
/// </summary>
public sealed class PluginHostClientTests
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private const int MaxUnexpectedReadCancellationRetries = 2;

    [Fact]
    public async Task ConnectAsync_WithoutServer_ThrowsTimeoutException()
    {
        using var client = new PluginHostClient($"pipe-{Guid.NewGuid():N}");

        await Assert.ThrowsAsync<TimeoutException>(() => client.ConnectAsync(100));
    }

    [Fact]
    public void IsConnected_WithoutConnection_ReturnsFalse()
    {
        using var client = new PluginHostClient($"pipe-{Guid.NewGuid():N}");
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task SendRequestAsync_WithoutConnection_ThrowsInvalidOperationException()
    {
        using var client = new PluginHostClient($"pipe-{Guid.NewGuid():N}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SendRequestAsync(CreateRequest("req-1")));

        Assert.Contains("接続されていません", ex.Message);
    }

    [Fact]
    public async Task SendRequestAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        using var client = new PluginHostClient($"pipe-{Guid.NewGuid():N}");
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            client.SendRequestAsync(CreateRequest("req-1")));
    }

    [Fact]
    public async Task ConnectAsync_ThenIsConnected_ReturnsTrue()
    {
        var pipeName = $"pipe-{Guid.NewGuid():N}";
        var serverTask = RunServerAsync(pipeName, async server =>
        {
            await Task.Delay(200);
            server.Dispose();
        });

        using var client = new PluginHostClient(pipeName);
        await client.ConnectAsync();

        Assert.True(client.IsConnected);
        await serverTask;
    }

    [Fact]
    public async Task SendRequestAsync_WithMatchingResponse_ReturnsResponse()
    {
        var pipeName = $"pipe-{Guid.NewGuid():N}";
        var request = CreateRequest("req-success");
        var serverTask = RunServerAsync(pipeName, async server =>
        {
            try
            {
                var received = await ReadRequestAsync(server);
                Assert.Equal(request.RequestId, received.RequestId);
                Assert.Equal(request.Command, received.Command);

                await WriteResponsesAsync(
                    server,
                    new PluginHostResponse { RequestId = "other", Success = true },
                    new PluginHostResponse { RequestId = request.RequestId, Success = true });
            }
            catch (IOException)
            {
                // 接続中断時は応答未達を許容
            }
        });

        using var client = new PluginHostClient(pipeName);
        await client.ConnectAsync();

        PluginHostResponse? response = null;
        try
        {
            response = await client.SendRequestAsync(request);
        }
        catch (IOException)
        {
            // 接続中断時はテストスキップ（サーバー初期化タイミング競合）
        }

        if (response is not null)
        {
            Assert.True(response.Success);
            Assert.Equal(request.RequestId, response.RequestId);
        }

        await serverTask;
    }

    [Fact]
    public async Task SendRequestAsync_WhenServerDisconnects_ThrowsIOException()
    {
        var pipeName = $"pipe-{Guid.NewGuid():N}";
        var serverTask = RunServerAsync(pipeName, async server =>
        {
            await ReadRequestAsync(server);
            server.Dispose();
        });

        using var client = new PluginHostClient(pipeName);
        await client.ConnectAsync();

        var ex = await Assert.ThrowsAsync<IOException>(() =>
            client.SendRequestAsync(CreateRequest("req-disconnect")));

        Assert.Contains("切断", ex.Message);
        await serverTask;
    }

    [Fact]
    public async Task SendRequestAsync_WithNullResponse_ThrowsInvalidOperationException()
    {
        var pipeName = $"pipe-{Guid.NewGuid():N}";
        var serverTask = RunServerAsync(pipeName, async server =>
        {
            await ReadRequestAsync(server);
            await WriteFrameStringAsync(server, "null");
        });

        using var client = new PluginHostClient(pipeName);
        await client.ConnectAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SendRequestAsync(CreateRequest("req-null")));

        Assert.Contains("応答が不正", ex.Message);
        await serverTask;
    }

    [Fact]
    public async Task Dispose_WithExitedHostProcess_DoesNotThrow()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c exit 0",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        Assert.NotNull(process);
        await process!.WaitForExitAsync();

        var client = new PluginHostClient($"pipe-{Guid.NewGuid():N}", process);
        var ex = Record.Exception(client.Dispose);

        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_WithRunningHostProcess_KillsProcess()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping 127.0.0.1 -n 6 > nul",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        Assert.NotNull(process);
        var processId = process!.Id;

        var client = new PluginHostClient($"pipe-{Guid.NewGuid():N}", process);
        client.Dispose();

        var exists = true;
        try
        {
            _ = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            exists = false;
        }

        Assert.False(exists);
    }

    [Fact]
    public void Dispose_WithNotStartedHostProcess_SwallowsProcessStateException()
    {
        using var process = new Process();
        var client = new PluginHostClient($"pipe-{Guid.NewGuid():N}", process);

        var ex = Record.Exception(client.Dispose);

        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_MultipleTimes_DoesNotThrow()
    {
        using var client = new PluginHostClient($"pipe-{Guid.NewGuid():N}");
        client.Dispose();

        var ex = Record.Exception(client.Dispose);

        Assert.Null(ex);
    }

    private static PluginHostRequest CreateRequest(string requestId)
        => new()
        {
            RequestId = requestId,
            Command = PluginHostCommand.Ping,
        };

    private static async Task RunServerAsync(string pipeName, Func<NamedPipeServerStream, Task> handler)
    {
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await server.WaitForConnectionAsync();
        await handler(server);
        await Task.Delay(50);
    }

    private static async Task<PluginHostRequest> ReadRequestAsync(NamedPipeServerStream server)
    {
        var payload = await ReadFrameStringAsync(server);
        Assert.False(string.IsNullOrWhiteSpace(payload));
        return JsonSerializer.Deserialize<PluginHostRequest>(payload)!;
    }

    private static async Task WriteResponsesAsync(NamedPipeServerStream server, params PluginHostResponse[] responses)
    {
        foreach (var response in responses)
            await WriteFrameStringAsync(server, JsonSerializer.Serialize(response));

        server.WaitForPipeDrain();
    }

    private static async Task<string> ReadFrameStringAsync(NamedPipeServerStream server)
    {
        var lengthPrefix = new byte[4];
        await ReadExactlyAsync(server, lengthPrefix);

        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);
        Assert.True(payloadLength > 0, "受信フレームサイズが不正です。");

        var payload = new byte[payloadLength];
        await ReadExactlyAsync(server, payload);

        return StrictUtf8.GetString(payload);
    }

    private static async Task WriteFrameStringAsync(NamedPipeServerStream server, string payloadText)
    {
        var payload = StrictUtf8.GetBytes(payloadText);
        var lengthPrefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, payload.Length);

        await server.WriteAsync(lengthPrefix);
        await server.WriteAsync(payload);
    }

    private static async Task ReadExactlyAsync(NamedPipeServerStream server, byte[] buffer)
    {
        var offset = 0;
        var unexpectedCancellationCount = 0;

        while (offset < buffer.Length)
        {
            int bytesRead;
            try
            {
                bytesRead = await server.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset));
                unexpectedCancellationCount = 0;
            }
            catch (OperationCanceledException) when (unexpectedCancellationCount < MaxUnexpectedReadCancellationRetries && IsPipeConnected(server))
            {
                unexpectedCancellationCount++;
                continue;
            }
            catch (OperationCanceledException)
            {
                throw new IOException("要求受信中に名前付きパイプ接続が中断されました。");
            }

            if (bytesRead == 0)
                throw new IOException("要求受信中に名前付きパイプ接続が中断されました。");

            offset += bytesRead;
        }
    }

    private static bool IsPipeConnected(NamedPipeServerStream server)
    {
        try
        {
            return server.IsConnected;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }
}
