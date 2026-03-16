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
    private const int MaxUnexpectedReadCancellationRetries = 2;

    [Fact]
    public async Task SendRequestAsync_WithImmediateResponse_CompletesQuickly()
    {
        var pipeName = $"pipe-{Guid.NewGuid():N}";
        var request = CreateRequest("req-fast");
        var serverTask = RunServerAsync(pipeName, async server =>
        {
            var received = await ReadRequestAsync(server);
            Assert.Equal(request.RequestId, received.RequestId);
            await WriteResponseAsync(server, new PluginHostResponse
            {
                RequestId = request.RequestId,
                Success = true,
            });
        });

        using var client = new PluginHostClient(pipeName);
        await client.ConnectAsync();
        var stopwatch = Stopwatch.StartNew();

        var response = await client.SendRequestAsync(request);

        stopwatch.Stop();
        Assert.True(response.Success);
        Assert.Equal(request.RequestId, response.RequestId);
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
        });

        using var client = new PluginHostClient(pipeName);
        await client.ConnectAsync();
        var stopwatch = Stopwatch.StartNew();

        var responses = await Task.WhenAll(requests.Select(request => client.SendRequestAsync(request)));

        stopwatch.Stop();
        Assert.Equal(requestCount, responses.Length);
        Assert.All(responses, response => Assert.True(response.Success));
        Assert.Equal(
            requests.Select(x => x.RequestId).OrderBy(x => x).ToArray(),
            responses.Select(x => x.RequestId).OrderBy(x => x).ToArray());
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
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);

        for (var retryCount = 0; ; retryCount++)
        {
            try
            {
                var line = await reader.ReadLineAsync();
                Assert.False(string.IsNullOrWhiteSpace(line));
                return JsonSerializer.Deserialize<PluginHostRequest>(line!)!;
            }
            catch (OperationCanceledException) when (retryCount < MaxUnexpectedReadCancellationRetries && IsPipeConnected(server))
            {
                // 断続的な pipe 読み取りキャンセルは少回数だけ再試行する
            }
            catch (OperationCanceledException)
            {
                throw new IOException("要求受信中に名前付きパイプ接続が中断されました。");
            }
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

    private static async Task WriteResponseAsync(NamedPipeServerStream server, PluginHostResponse response)
    {
        using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true,
        };

        await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        server.WaitForPipeDrain();
    }
}
