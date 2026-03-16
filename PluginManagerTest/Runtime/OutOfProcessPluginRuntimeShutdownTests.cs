using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using PluginManager;
using PluginManager.Ipc;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="OutOfProcessPluginRuntime"/> のシャットダウン待機タイムアウトに関するテストです。
/// </summary>
public sealed class OutOfProcessPluginRuntimeShutdownTests
{
    private const int MaxUnexpectedReadCancellationRetries = 2;

    [Fact]
    public async Task UnloadAll_WithUnresponsiveShutdown_ReturnsWithinTimeout()
    {
        var pipeName = $"pipe-{Guid.NewGuid():N}";
        var assemblyPath = $"assembly-{Guid.NewGuid():N}.dll";
        var requests = new ConcurrentQueue<PluginHostRequest>();
        var serverTask = RunServerWithoutShutdownResponseAsync(pipeName, requests, respondToUnload: false);

        using var runtime = new OutOfProcessPluginRuntime();
        using var client = new PluginHostClient(pipeName);
        await client.ConnectAsync();
        GetClients(runtime)[assemblyPath] = client;

        var stopwatch = Stopwatch.StartNew();
        runtime.UnloadAll();
        stopwatch.Stop();

        await serverTask;

        Assert.Equal([PluginHostCommand.Shutdown], requests.Select(x => x.Command).ToArray());
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "Shutdown 応答がなくても短時間で復帰するべきです。");
    }

    [Fact]
    public async Task UnloadAsync_WithUnresponsiveShutdown_ReturnsWithinTimeout()
    {
        var pipeName = $"pipe-{Guid.NewGuid():N}";
        var queueName = $"queue-{Guid.NewGuid():N}";
        var assemblyPath = $"assembly-{Guid.NewGuid():N}.dll";
        var requests = new ConcurrentQueue<PluginHostRequest>();
        var serverTask = RunServerWithoutShutdownResponseAsync(pipeName, requests, respondToUnload: true);

        using var queue = new MemoryMappedNotificationQueue(queueName);
        using var client = new PluginHostClient(pipeName);
        await client.ConnectAsync();

        using var runtime = new OutOfProcessPluginRuntime();
        SetRuntimeState(runtime, assemblyPath, CreateProxy(client, queue, assemblyPath));

        var stopwatch = Stopwatch.StartNew();
        await runtime.UnloadAsync(assemblyPath);
        stopwatch.Stop();

        await serverTask;

        Assert.Equal([PluginHostCommand.Unload, PluginHostCommand.Shutdown], requests.Select(x => x.Command).ToArray());
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "Shutdown 応答がなくても短時間で復帰するべきです。");
    }

    [Fact]
    public void SetShutdownTimeoutMilliseconds_UpdatesRuntimeValue()
    {
        using var runtime = new OutOfProcessPluginRuntime();

        runtime.SetShutdownTimeoutMilliseconds(4500);

        var field = typeof(OutOfProcessPluginRuntime).GetField("_shutdownTimeoutMilliseconds", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.Equal(4500, field!.GetValue(runtime));
    }

    private static ConcurrentDictionary<string, PluginHostClient> GetClients(OutOfProcessPluginRuntime runtime)
    {
        var field = typeof(OutOfProcessPluginRuntime).GetField("_clients", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (ConcurrentDictionary<string, PluginHostClient>)field!.GetValue(runtime)!;
    }

    private static void SetRuntimeState(OutOfProcessPluginRuntime runtime, string assemblyPath, OutOfProcessPluginProxy proxy)
    {
        GetClients(runtime)[assemblyPath] = proxy.Client;

        var queuesField = typeof(OutOfProcessPluginRuntime).GetField("_notificationQueues", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(queuesField);
        var queues = (ConcurrentDictionary<string, MemoryMappedNotificationQueue>)queuesField!.GetValue(runtime)!;
        queues[assemblyPath] = GetProxyQueue(proxy);

        var proxiesField = typeof(OutOfProcessPluginRuntime).GetField("_proxies", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(proxiesField);
        var proxies = (ConcurrentDictionary<string, OutOfProcessPluginProxy>)proxiesField!.GetValue(runtime)!;
        proxies[proxy.Id] = proxy;
    }

    private static MemoryMappedNotificationQueue GetProxyQueue(OutOfProcessPluginProxy proxy)
    {
        var field = typeof(OutOfProcessPluginProxy).GetField("_notificationQueue", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (MemoryMappedNotificationQueue)field!.GetValue(proxy)!;
    }

    private static OutOfProcessPluginProxy CreateProxy(PluginHostClient client, MemoryMappedNotificationQueue queue, string assemblyPath)
        => new(
            new PluginDescriptor(
                "oop-timeout-test",
                "OutOfProcessTimeoutTest",
                new Version(1, 0, 0),
                typeof(object).FullName!,
                assemblyPath,
                new[] { PluginStage.Processing }.ToFrozenSet()),
            client,
            queue,
            _ => { });

    private static async Task RunServerWithoutShutdownResponseAsync(
        string pipeName,
        ConcurrentQueue<PluginHostRequest> requests,
        bool respondToUnload)
    {
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await server.WaitForConnectionAsync();

        if (respondToUnload)
        {
            var unloadRequest = await ReadRequestAsync(server);
            requests.Enqueue(unloadRequest);
            await WriteResponseAsync(server, new PluginHostResponse { RequestId = unloadRequest.RequestId, Success = true });
        }

        var shutdownRequest = await ReadRequestAsync(server);
        requests.Enqueue(shutdownRequest);

        var buffer = new byte[1];
        while (await server.ReadAsync(buffer) > 0)
        {
        }
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
        using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        server.WaitForPipeDrain();
    }
}
