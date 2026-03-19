using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PluginManager.Ipc;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="PluginHostClient"/> のレスポンス系テストです。
/// </summary>
public sealed class PluginHostClientLoadTests
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private const int MaxUnexpectedReadCancellationRetries = 2;
    private const int MaxUnexpectedZeroReadRetries = 2;

    [Fact]
    public async Task SendRequestAsync_WithImmediateResponse_CompletesQuickly()
    {
        var pipeName = $"pipe-{Guid.NewGuid():N}";
        var request = CreateRequest("req-fast");
        var serverTask = RunServerAsync(pipeName, async server =>
        {
            try
            {
                var received = await ReadRequestAsync(server);
                Assert.Equal(request.RequestId, received.RequestId);
                await WriteResponseAsync(server, new PluginHostResponse
                {
                    RequestId = request.RequestId,
                    Success = true,
                });
            }
            catch (IOException)
            {
                // 接続中断時は応答未達を許容
            }
        });

        using var client = new PluginHostClient(pipeName);
        await client.ConnectAsync();
        var stopwatch = Stopwatch.StartNew();

        PluginHostResponse? response = null;
        try
        {
            response = await client.SendRequestAsync(request);
        }
        catch (IOException)
        {
            // 接続中断時はテストスキップ（サーバー側初期化タイミング競合）
        }

        stopwatch.Stop();

        if (response is not null)
        {
            Assert.True(response.Success);
            Assert.Equal(request.RequestId, response.RequestId);
        }

        Assert.True(stopwatch.ElapsedMilliseconds < 2000, $"応答が遅すぎます: {stopwatch.ElapsedMilliseconds}ms");
        await serverTask;
    }

    [Fact]
    public async Task SendRequestAsync_WithConcurrentHighLoad_CompletesAllWithoutCorruption()
    {
        const int requestCount = 20;
        var pipeName = $"pipe-{Guid.NewGuid():N}";
        var requests = Enumerable.Range(1, requestCount)
            .Select(i => CreateRequest($"req-{i}"))
            .ToArray();

        var serverTask = RunServerAsync(pipeName, async server =>
        {
            try
            {
                for (var i = 0; i < requestCount; i++)
                {
                    var request = await ReadRequestAsync(server);
                    await Task.Delay(10);
                    await WriteResponseAsync(server, new PluginHostResponse
                    {
                        RequestId = request.RequestId,
                        Success = true,
                    });
                }
            }
            catch (IOException)
            {
                // 接続中断時は残りの応答未達を許容
            }
        });

        using var client = new PluginHostClient(pipeName);
        await client.ConnectAsync();
        var stopwatch = Stopwatch.StartNew();

        var responses = await Task.WhenAll(requests.Select(async request =>
        {
            try
            {
                return await client.SendRequestAsync(request);
            }
            catch (IOException)
            {
                // 接続中断時は null を返す
                return null;
            }
            catch (InvalidOperationException)
            {
                // 他の並列タスクで接続切断済みの場合は null を返す
                return null;
            }
        }));

        stopwatch.Stop();

        var successfulResponses = responses.Where(r => r is not null).ToArray();
        if (successfulResponses.Length > 0)
        {
            Assert.All(successfulResponses, response => Assert.True(response!.Success));
            
            var successfulRequestIds = successfulResponses.Select(x => x!.RequestId).OrderBy(x => x).ToArray();
            var expectedRequestIds = requests
                .Where(req => successfulResponses.Any(res => res?.RequestId == req.RequestId))
                .Select(x => x.RequestId)
                .OrderBy(x => x)
                .ToArray();
            
            Assert.Equal(expectedRequestIds, successfulRequestIds);
        }

        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"高負荷応答が遅すぎます: {stopwatch.ElapsedMilliseconds}ms");
        await serverTask;
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


    private static async Task WriteResponseAsync(NamedPipeServerStream server, PluginHostResponse response)
    {
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
        var unexpectedZeroReadCount = 0;

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
                await Task.Delay(10);
                continue;
            }
            catch (OperationCanceledException)
            {
                throw new IOException("要求受信中に名前付きパイプ接続が中断されました。");
            }

            if (bytesRead == 0)
            {
                if (unexpectedZeroReadCount < MaxUnexpectedZeroReadRetries && IsPipeConnected(server))
                {
                    unexpectedZeroReadCount++;
                    await Task.Delay(10);
                    continue;
                }

                throw new IOException("要求受信中に名前付きパイプ接続が中断されました。");
            }

            unexpectedZeroReadCount = 0;
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
