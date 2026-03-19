using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using PluginManager;
using PluginManager.Ipc;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="OutOfProcessPluginRuntime"/> の負荷後リソース健全性テストです。
/// </summary>
public sealed class OutOfProcessRuntimeResourceTests
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private const int MaxUnexpectedReadCancellationRetries = 2;

    [Fact]
    public async Task UnloadAll_AfterMultipleClients_ClearsInternalState()
    {
        const int clientCount = 5;
        var runtimes = new List<(string AssemblyPath, PluginHostClient Client, MemoryMappedNotificationQueue Queue, Task ServerTask)>();
        var callback = new TestProcessCallback();
        var publisher = new PluginProcessNotificationPublisher(logger: null);
        publisher.SetCallback(callback);
        using var runtime = new OutOfProcessPluginRuntime(publisher);

        try
        {
            for (var i = 0; i < clientCount; i++)
            {
                var pipeName = $"pipe-{Guid.NewGuid():N}";
                var queueName = $"queue-{Guid.NewGuid():N}";
                var assemblyPath = $"assembly-{Guid.NewGuid():N}.dll";
                var pluginId = Path.GetFileNameWithoutExtension(assemblyPath);
                var serverTask = RunServerAsync(pipeName, async server =>
                {
                    var shutdownRequest = await ReadRequestAsync(server);
                    Assert.Equal(PluginHostCommand.Shutdown, shutdownRequest.Command);
                    await WriteResponseAsync(server, new PluginHostResponse
                    {
                        RequestId = shutdownRequest.RequestId,
                        Success = true,
                    });
                });

                var queue = new MemoryMappedNotificationQueue(queueName);
                var client = new PluginHostClient(pipeName);
                await client.ConnectAsync();
                queue.Enqueue(new PluginProcessNotification
                {
                    NotificationType = PluginProcessNotificationType.ShutdownReceived,
                    Message = "終了要求",
                    ProcessId = i + 1,
                });

                SetRuntimeState(runtime, assemblyPath, CreateProxy(client, queue, assemblyPath, pluginId));
                runtimes.Add((assemblyPath, client, queue, serverTask));
            }

            runtime.UnloadAll();

            await Task.WhenAll(runtimes.Select(x => x.ServerTask));
            Assert.Equal(clientCount, callback.Notifications.Count(x => x.NotificationType == PluginProcessNotificationType.ShutdownReceived));
            Assert.Empty(GetClients(runtime));
            Assert.Empty(GetQueues(runtime));
            Assert.Empty(GetProxies(runtime));
        }
        finally
        {
            foreach (var entry in runtimes)
            {
                entry.Client.Dispose();
                entry.Queue.Dispose();
            }
        }
    }

    [Fact]
    public async Task UnloadAsync_AfterMultipleProxies_RemovesOnlyTargetAssemblyState()
    {
        var callback = new TestProcessCallback();
        var publisher = new PluginProcessNotificationPublisher(logger: null);
        publisher.SetCallback(callback);
        using var runtime = new OutOfProcessPluginRuntime(publisher);

        var first = await CreateRegisteredProxyAsync(runtime, "assembly-a.dll", 1);
        var second = await CreateRegisteredProxyAsync(runtime, "assembly-b.dll", 2);

        try
        {
            await runtime.UnloadAsync(first.AssemblyPath);
            await first.ServerTask;

            Assert.False(GetClients(runtime).ContainsKey(first.AssemblyPath));
            Assert.False(GetQueues(runtime).ContainsKey(first.AssemblyPath));
            Assert.True(GetClients(runtime).ContainsKey(second.AssemblyPath));
            Assert.True(GetQueues(runtime).ContainsKey(second.AssemblyPath));
            Assert.NotEmpty(callback.Notifications);
        }
        finally
        {
            await runtime.UnloadAsync(second.AssemblyPath);
            await second.ServerTask;
            second.Client.Dispose();
            second.Queue.Dispose();
        }
    }

    private static async Task<(string AssemblyPath, PluginHostClient Client, MemoryMappedNotificationQueue Queue, Task ServerTask)> CreateRegisteredProxyAsync(
        OutOfProcessPluginRuntime runtime,
        string assemblyPath,
        int processId)
    {
        var pipeName = $"pipe-{Guid.NewGuid():N}";
        var queueName = $"queue-{Guid.NewGuid():N}";
        var pluginId = Path.GetFileNameWithoutExtension(assemblyPath);
        var serverTask = RunServerAsync(pipeName, async server =>
        {
            var unloadRequest = await ReadRequestAsync(server);
            Assert.Equal(PluginHostCommand.Unload, unloadRequest.Command);
            await WriteResponseAsync(server, new PluginHostResponse
            {
                RequestId = unloadRequest.RequestId,
                Success = true,
            });

            var shutdownRequest = await ReadRequestAsync(server);
            Assert.Equal(PluginHostCommand.Shutdown, shutdownRequest.Command);
            await WriteResponseAsync(server, new PluginHostResponse
            {
                RequestId = shutdownRequest.RequestId,
                Success = true,
            });
        });

        var queue = new MemoryMappedNotificationQueue(queueName);
        var client = new PluginHostClient(pipeName);
        await client.ConnectAsync();
        queue.Enqueue(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.UnloadCompleted,
            Message = "アンロード完了",
            PluginId = pluginId,
            ProcessId = processId,
        });
        queue.Enqueue(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.ShutdownReceived,
            Message = "終了要求",
            ProcessId = processId,
        });

        SetRuntimeState(runtime, assemblyPath, CreateProxy(client, queue, assemblyPath, pluginId));
        return (assemblyPath, client, queue, serverTask);
    }

    private static OutOfProcessPluginProxy CreateProxy(PluginHostClient client, MemoryMappedNotificationQueue queue, string assemblyPath, string pluginId)
        => new(
            new PluginDescriptor(pluginId, "OutOfProcessTest", new Version(1, 0, 0), typeof(TestPlugin).FullName!, assemblyPath, new[] { PluginStage.Processing }.ToFrozenSet()),
            client,
            queue,
            _ => { });

    private static void SetRuntimeState(OutOfProcessPluginRuntime runtime, string assemblyPath, OutOfProcessPluginProxy proxy)
    {
        GetClients(runtime)[assemblyPath] = proxy.Client;
        GetQueues(runtime)[assemblyPath] = GetField<MemoryMappedNotificationQueue>(proxy, "_notificationQueue");
        GetProxies(runtime)[proxy.Id] = proxy;
    }

    private static ConcurrentDictionary<string, PluginHostClient> GetClients(OutOfProcessPluginRuntime runtime)
        => GetField<ConcurrentDictionary<string, PluginHostClient>>(runtime, "_clients");

    private static ConcurrentDictionary<string, MemoryMappedNotificationQueue> GetQueues(OutOfProcessPluginRuntime runtime)
        => GetField<ConcurrentDictionary<string, MemoryMappedNotificationQueue>>(runtime, "_notificationQueues");

    private static ConcurrentDictionary<string, OutOfProcessPluginProxy> GetProxies(OutOfProcessPluginRuntime runtime)
        => GetField<ConcurrentDictionary<string, OutOfProcessPluginProxy>>(runtime, "_proxies");

    private static T GetField<T>(object instance, string fieldName) where T : class
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field!.GetValue(instance)!;
    }

    private static async Task RunServerAsync(string pipeName, Func<NamedPipeServerStream, Task> handler)
    {
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await server.WaitForConnectionAsync();
        await handler(server);
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

    private sealed class TestProcessCallback : IPluginProcessCallback
    {
        public List<PluginProcessNotification> Notifications { get; } = [];
        public void OnNotification(PluginProcessNotification notification) => Notifications.Add(notification);
        public void OnUnloadCompleted(string pluginId) { }
        public void OnShutdownReceived() { }
    }

    private sealed class TestPlugin : IPlugin
    {
        public string Id => "oop-test";
        public string Name => "OutOfProcessTest";
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } = new[] { PluginStage.Processing }.ToFrozenSet();
        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default) => Task.FromResult<object?>("ok");
    }
}
