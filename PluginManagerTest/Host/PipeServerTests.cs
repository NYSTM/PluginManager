using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PluginHost;
using PluginManager.Ipc;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="PipeServer"/> の名前付きパイプ処理テストです。
/// </summary>
public sealed class PipeServerTests
{
    private const int MaxUnexpectedReadCancellationRetries = 2;
    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public async Task RunAsync_WhenCanceled_StopsGracefully()
    {
        var pipeName = $"PipeServerTests_{Guid.NewGuid():N}";
        using var registry = new PluginRegistry(new PluginHostNotifier());
        var handler = new PluginRequestHandler(registry, new PluginHostNotifier());
        var server = new PipeServer(pipeName, handler, new PluginHostNotifier());
        using var cts = new CancellationTokenSource();

        var runTask = server.RunAsync(cts.Token);

        await Task.Delay(150);
        cts.Cancel();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunAsync_PingRequest_ReturnsSuccessResponse()
    {
        var pipeName = $"PipeServerTests_{Guid.NewGuid():N}";
        using var registry = new PluginRegistry(new PluginHostNotifier());
        var handler = new PluginRequestHandler(registry, new PluginHostNotifier());
        var server = new PipeServer(pipeName, handler, new PluginHostNotifier());
        using var cts = new CancellationTokenSource();

        var runTask = server.RunAsync(cts.Token);

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000, CancellationToken.None);
        await using var writer = new StreamWriter(client, _utf8NoBom, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, _utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

        var request = new PluginHostRequest
        {
            RequestId = "req-pipe-ping",
            Command = PluginHostCommand.Ping,
        };

        await writer.WriteLineAsync(JsonSerializer.Serialize(request));
        var responseJson = await ReadPipeLineAsync(reader, client).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(responseJson);
        var response = JsonSerializer.Deserialize<PluginHostResponse>(responseJson!);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal("req-pipe-ping", response.RequestId);

        cts.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunAsync_InvalidJsonThenPing_ContinuesAndPublishesFailureNotification()
    {
        var mapName = $"PipeServerTests_{Guid.NewGuid():N}";
        var pipeName = $"PipeServerTests_{Guid.NewGuid():N}";
        using var queueWriter = new MemoryMappedNotificationQueue(mapName);
        using var queueReader = new MemoryMappedNotificationQueue(mapName);
        var notifier = new PluginHostNotifier(queueWriter);
        using var registry = new PluginRegistry(notifier);
        var handler = new PluginRequestHandler(registry, notifier);
        var server = new PipeServer(pipeName, handler, notifier);
        using var cts = new CancellationTokenSource();

        var runTask = server.RunAsync(cts.Token);

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000, CancellationToken.None);
        await using var writer = new StreamWriter(client, _utf8NoBom, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, _utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

        await writer.WriteLineAsync("{invalid-json}");
        await Task.Delay(100);

        var pingRequest = new PluginHostRequest
        {
            RequestId = "req-pipe-after-invalid",
            Command = PluginHostCommand.Ping,
        };

        await writer.WriteLineAsync(JsonSerializer.Serialize(pingRequest));
        var responseJson = await ReadPipeLineAsync(reader, client).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(responseJson);
        var response = JsonSerializer.Deserialize<PluginHostResponse>(responseJson!);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal("req-pipe-after-invalid", response.RequestId);

        var notifications = queueReader.Drain();
        Assert.Contains(notifications, n => n.NotificationType == PluginProcessNotificationType.RequestProcessingFailed);

        cts.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunAsync_BlankLineThenPing_ReturnsSuccessResponse()
    {
        var pipeName = $"PipeServerTests_{Guid.NewGuid():N}";
        using var registry = new PluginRegistry(new PluginHostNotifier());
        var handler = new PluginRequestHandler(registry, new PluginHostNotifier());
        var server = new PipeServer(pipeName, handler, new PluginHostNotifier());
        using var cts = new CancellationTokenSource();

        var runTask = server.RunAsync(cts.Token);

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000, CancellationToken.None);
        await using var writer = new StreamWriter(client, _utf8NoBom, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, _utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

        await writer.WriteLineAsync("   ");

        var pingRequest = new PluginHostRequest
        {
            RequestId = "req-blank-ping",
            Command = PluginHostCommand.Ping,
        };

        await writer.WriteLineAsync(JsonSerializer.Serialize(pingRequest));
        var responseJson = await ReadPipeLineAsync(reader, client).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(responseJson);
        var response = JsonSerializer.Deserialize<PluginHostResponse>(responseJson!);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal("req-blank-ping", response.RequestId);

        cts.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task<string?> ReadPipeLineAsync(StreamReader reader, NamedPipeClientStream client)
    {
        for (var retryCount = 0; ; retryCount++)
        {
            try
            {
                return await reader.ReadLineAsync();
            }
            catch (OperationCanceledException) when (retryCount < MaxUnexpectedReadCancellationRetries && IsPipeConnected(client))
            {
                // 断続的な pipe 読み取りキャンセルは少回数だけ再試行する
            }
            catch (OperationCanceledException)
            {
                throw new IOException("応答受信中に名前付きパイプ接続が中断されました。");
            }
        }
    }

    private static bool IsPipeConnected(NamedPipeClientStream client)
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
}
