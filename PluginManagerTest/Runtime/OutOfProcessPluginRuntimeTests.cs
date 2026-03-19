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
/// OutOfProcess 隔離モードの統合テストです。
/// </summary>
public sealed class OutOfProcessPluginRuntimeTests
{
    private const int MaxUnexpectedReadCancellationRetries = 2;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    [Fact]
    public void PluginMetadata_OutOfProcessIsolationMode_IsConfigurable()
    {
        var attribute = new PluginAttribute("test-id", "Test", "1.0.0")
        {
            IsolationMode = PluginIsolationMode.OutOfProcess,
        };
        Assert.Equal(PluginIsolationMode.OutOfProcess, attribute.IsolationMode);

        var descriptor = new PluginDescriptor(
            "test-id", "Test", new Version(1, 0, 0),
            typeof(object).FullName!, "test.dll",
            new[] { PluginStage.Processing }.ToFrozenSet())
        {
            IsolationMode = PluginIsolationMode.OutOfProcess,
        };
        Assert.Equal(PluginIsolationMode.OutOfProcess, descriptor.IsolationMode);
    }

    [Fact]
    public void IsolationMode_ReturnsOutOfProcess()
    {
        using var runtime = new OutOfProcessPluginRuntime();
        Assert.Equal(PluginIsolationMode.OutOfProcess, runtime.IsolationMode);
    }

    [Fact]
    public async Task LoadAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var runtime = new OutOfProcessPluginRuntime();
        var descriptor = CreateDescriptor();
        runtime.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            runtime.LoadAsync(descriptor, new PluginContext(), CancellationToken.None));
    }

    [Fact]
    public void Unload_NonExistentAssembly_DoesNotThrow()
    {
        using var runtime = new OutOfProcessPluginRuntime();
        var ex = Record.Exception(() => runtime.Unload("missing.dll"));
        Assert.Null(ex);
    }

    [Fact]
    public async Task UnloadAsync_NonExistentAssembly_DoesNotThrow()
    {
        using var runtime = new OutOfProcessPluginRuntime();
        var ex = await Record.ExceptionAsync(() => runtime.UnloadAsync("missing.dll"));
        Assert.Null(ex);
    }

    [Fact]
    public void UnloadAll_WithoutClients_DoesNotThrow()
    {
        using var runtime = new OutOfProcessPluginRuntime();
        var ex = Record.Exception(runtime.UnloadAll);
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_MultipleTimes_DoesNotThrow()
    {
        var runtime = new OutOfProcessPluginRuntime();
        runtime.Dispose();

        var ex = Record.Exception(runtime.Dispose);

        Assert.Null(ex);
    }

    [Fact]
    public async Task Unload_WithRegisteredProxy_SendsUnloadAndShutdown()
    {
        var pipeName = $"pipe-{Guid.NewGuid():N}";
        var queueName = $"queue-{Guid.NewGuid():N}";
        var assemblyPath = $"assembly-{Guid.NewGuid():N}.dll";
        var requests = new ConcurrentQueue<PluginHostRequest>();
        var callback = new TestProcessCallback();
        var publisher = new PluginProcessNotificationPublisher(logger: null);
        publisher.SetCallback(callback);
        var serverTask = RunServerAsync(pipeName, async server =>
        {
            var unloadRequest = await ReadRequestAsync(server);
            requests.Enqueue(unloadRequest);
            await WriteResponseAsync(server, new PluginHostResponse { RequestId = unloadRequest.RequestId, Success = true });

            var shutdownRequest = await ReadRequestAsync(server);
            requests.Enqueue(shutdownRequest);
            await WriteResponseAsync(server, new PluginHostResponse { RequestId = shutdownRequest.RequestId, Success = true });
        });

        using var queue = new MemoryMappedNotificationQueue(queueName);
        using var client = new PluginHostClient(pipeName);
        await client.ConnectAsync();
        queue.Enqueue(new PluginProcessNotification { NotificationType = PluginProcessNotificationType.UnloadCompleted, Message = "アンロード完了", PluginId = "oop-test", ProcessId = 1 });
        queue.Enqueue(new PluginProcessNotification { NotificationType = PluginProcessNotificationType.ShutdownReceived, Message = "終了要求", ProcessId = 1 });

        using var runtime = new OutOfProcessPluginRuntime(publisher);
        SetRuntimeState(runtime, assemblyPath, CreateProxy(client, queue, assemblyPath, _ => { }));

        runtime.Unload(assemblyPath);
        await serverTask;

        Assert.Equal([PluginHostCommand.Unload, PluginHostCommand.Shutdown], requests.Select(x => x.Command).ToArray());
        Assert.Equal([PluginProcessNotificationType.UnloadCompleted, PluginProcessNotificationType.ShutdownReceived], callback.Notifications.Select(x => x.NotificationType).ToArray());
        Assert.True(callback.ShutdownReceived);
        Assert.Equal("oop-test", callback.UnloadedPluginId);
    }

    [Fact]
    public void Unload_WithDisconnectedClient_SwallowsExceptions()
    {
        var pipeName = $"pipe-{Guid.NewGuid():N}";
        var queueName = $"queue-{Guid.NewGuid():N}";
        var assemblyPath = $"assembly-{Guid.NewGuid():N}.dll";

        using var queue = new MemoryMappedNotificationQueue(queueName);
        using var client = new PluginHostClient(pipeName);
        using var runtime = new OutOfProcessPluginRuntime();
        SetRuntimeState(runtime, assemblyPath, CreateProxy(client, queue, assemblyPath, _ => { }));

        var ex = Record.Exception(() => runtime.Unload(assemblyPath));

        Assert.Null(ex);
    }

    [Fact]
    public async Task UnloadAsync_WithRegisteredProxy_SendsUnloadAndShutdown()
    {
        var pipeName = $"pipe-{Guid.NewGuid():N}";
        var queueName = $"queue-{Guid.NewGuid():N}";
        var assemblyPath = $"assembly-{Guid.NewGuid():N}.dll";
        var requests = new ConcurrentQueue<PluginHostRequest>();
        var callback = new TestProcessCallback();
        var publisher = new PluginProcessNotificationPublisher(logger: null);
        publisher.SetCallback(callback);
        var serverTask = RunServerAsync(pipeName, async server =>
        {
            try
            {
                var unloadRequest = await ReadRequestAsync(server);
                requests.Enqueue(unloadRequest);
                await WriteResponseAsync(server, new PluginHostResponse { RequestId = unloadRequest.RequestId, Success = true });

                var shutdownRequest = await ReadRequestAsync(server);
                requests.Enqueue(shutdownRequest);
                await WriteResponseAsync(server, new PluginHostResponse { RequestId = shutdownRequest.RequestId, Success = true });
            }
            catch (IOException)
            {
                // 接続中断時は Unload/Shutdown 応答未達を許容する
            }
        });

        using var queue = new MemoryMappedNotificationQueue(queueName);
        using var client = new PluginHostClient(pipeName);
        await client.ConnectAsync();
        queue.Enqueue(new PluginProcessNotification { NotificationType = PluginProcessNotificationType.UnloadCompleted, Message = "アンロード完了", PluginId = "oop-test", ProcessId = 1 });
        queue.Enqueue(new PluginProcessNotification { NotificationType = PluginProcessNotificationType.ShutdownReceived, Message = "終了要求", ProcessId = 1 });

        using var runtime = new OutOfProcessPluginRuntime(publisher);
        SetRuntimeState(runtime, assemblyPath, CreateProxy(client, queue, assemblyPath, _ => { }));

        await runtime.UnloadAsync(assemblyPath);
        await serverTask;

        if (requests.Any())
        {
            var commands = requests.Select(x => x.Command).ToArray();
            Assert.Equal(PluginHostCommand.Unload, commands[0]);
        }

        Assert.Equal(
            [PluginProcessNotificationType.UnloadCompleted, PluginProcessNotificationType.ShutdownReceived],
            callback.Notifications.Select(x => x.NotificationType).ToArray());
        Assert.True(callback.ShutdownReceived);
        Assert.Equal("oop-test", callback.UnloadedPluginId);
    }

    [Fact]
    public async Task UnloadAll_WithRegisteredClient_SendsShutdownAndPublishesNotifications()
    {
        var pipeName = $"pipe-{Guid.NewGuid():N}";
        var queueName = $"queue-{Guid.NewGuid():N}";
        var assemblyPath = $"assembly-{Guid.NewGuid():N}.dll";
        var requests = new ConcurrentQueue<PluginHostRequest>();
        var callback = new TestProcessCallback();
        var publisher = new PluginProcessNotificationPublisher(logger: null);
        publisher.SetCallback(callback);
        var serverTask = RunServerAsync(pipeName, async server =>
        {
            var shutdownRequest = await ReadRequestAsync(server);
            requests.Enqueue(shutdownRequest);
            await WriteResponseAsync(server, new PluginHostResponse { RequestId = shutdownRequest.RequestId, Success = true });
        });

        using var queue = new MemoryMappedNotificationQueue(queueName);
        using var client = new PluginHostClient(pipeName);
        await client.ConnectAsync();
        queue.Enqueue(new PluginProcessNotification { NotificationType = PluginProcessNotificationType.ShutdownReceived, Message = "終了要求", ProcessId = 1 });

        using var runtime = new OutOfProcessPluginRuntime(publisher);
        SetRuntimeState(runtime, assemblyPath, CreateProxy(client, queue, assemblyPath, _ => { }));

        runtime.UnloadAll();
        await serverTask;

        Assert.Equal([PluginHostCommand.Shutdown], requests.Select(x => x.Command).ToArray());
        Assert.Equal([PluginProcessNotificationType.ShutdownReceived], callback.Notifications.Select(x => x.NotificationType).ToArray());
        Assert.True(callback.ShutdownReceived);
    }

    [Fact]
    public void UnloadAll_WithDisposedClient_SwallowsShutdownException()
    {
        var assemblyPath = $"assembly-{Guid.NewGuid():N}.dll";
        var runtime = new OutOfProcessPluginRuntime();
        var clients = GetField<ConcurrentDictionary<string, PluginHostClient>>(runtime, "_clients");
        var disposedClient = new PluginHostClient($"pipe-{Guid.NewGuid():N}");
        disposedClient.Dispose();
        clients[assemblyPath] = disposedClient;

        var ex = Record.Exception(runtime.UnloadAll);

        Assert.Null(ex);
        runtime.Dispose();
    }

    [Fact]
    public void PublishQueuedNotifications_WithNullQueue_DoesNothing()
    {
        using var runtime = new OutOfProcessPluginRuntime();

        var ex = Record.Exception(() => InvokePublishQueuedNotifications(runtime, null));

        Assert.Null(ex);
    }

    [Fact]
    public void PublishNotification_WithoutPublisher_DoesNothing()
    {
        using var runtime = new OutOfProcessPluginRuntime();
        var notification = new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.ShutdownReceived,
            Message = "終了要求",
        };

        var ex = Record.Exception(() => InvokePublishNotification(runtime, notification));

        Assert.Null(ex);
    }

    [Fact]
    public void PublishNotification_WithPublisher_ForwardsNotification()
    {
        var callback = new TestProcessCallback();
        var publisher = new PluginProcessNotificationPublisher(logger: null);
        publisher.SetCallback(callback);
        using var runtime = new OutOfProcessPluginRuntime(publisher);
        var notification = new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.ShutdownReceived,
            Message = "終了要求",
            ProcessId = 1,
        };

        InvokePublishNotification(runtime, notification);

        Assert.Single(callback.Notifications);
        Assert.True(callback.ShutdownReceived);
    }

    [Fact]
    public void FindPluginHostExecutable_WithExecutableInBasePath_ReturnsBasePathExecutable()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"oop-findhost-base-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);
        var exePath = Path.Combine(baseDir, "PluginHost.exe");
        File.WriteAllText(exePath, "stub");

        try
        {
            var resolved = InvokeFindPluginHostExecutable(baseDir);
            Assert.Equal(exePath, resolved, ignoreCase: true);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void FindPluginHostExecutable_WithExecutableInFallbackPath_ReturnsFallbackExecutable()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"oop-findhost-fallback-base-{Guid.NewGuid():N}");
        var fallbackDir = Path.GetFullPath(Path.Combine(baseDir, "..", "PluginHost"));
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(fallbackDir);
        var fallbackExe = Path.Combine(fallbackDir, "PluginHost.exe");
        File.WriteAllText(fallbackExe, "stub");

        try
        {
            var resolved = InvokeFindPluginHostExecutable(baseDir);
            Assert.Equal(Path.GetFullPath(fallbackExe), resolved, ignoreCase: true);
        }
        finally
        {
            if (Directory.Exists(baseDir))
                Directory.Delete(baseDir, recursive: true);
            if (Directory.Exists(fallbackDir))
                Directory.Delete(fallbackDir, recursive: true);
        }
    }

    [Fact]
    public void FindPluginHostExecutable_WhenNotFound_ThrowsFileNotFoundException()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"oop-findhost-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var ex = Assert.Throws<TargetInvocationException>(() => InvokeFindPluginHostExecutable(baseDir));
            Assert.IsType<FileNotFoundException>(ex.InnerException);
            Assert.Contains("PluginHost.exe", ex.InnerException!.Message);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Theory]
    [InlineData(nameof(InvalidOperationException), typeof(InvalidOperationException))]
    [InlineData(nameof(ArgumentException), typeof(ArgumentException))]
    [InlineData(nameof(NotSupportedException), typeof(NotSupportedException))]
    [InlineData("CustomException", typeof(Exception))]
    public void CreateExceptionFromResponse_MapsErrorType(string? errorType, Type expectedType)
    {
        var response = new PluginHostResponse
        {
            RequestId = "req-1",
            Success = false,
            ErrorType = errorType,
            ErrorMessage = "error-message",
        };

        var exception = InvokeCreateExceptionFromResponse(response);

        Assert.IsType(expectedType, exception);
        Assert.Equal("error-message", exception.Message);
    }

    [Fact]
    public void CreateExceptionFromResponse_WithoutErrorMessage_UsesDefaultMessage()
    {
        var response = new PluginHostResponse
        {
            RequestId = "req-2",
            Success = false,
            ErrorType = null,
            ErrorMessage = null,
        };

        var exception = InvokeCreateExceptionFromResponse(response);

        Assert.IsType<Exception>(exception);
        Assert.Equal("不明なエラー", exception.Message);
    }

    private static string InvokeFindPluginHostExecutable(string basePath)
    {
        var method = typeof(OutOfProcessPluginRuntime).GetMethod(
            "FindPluginHostExecutable",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(string)],
            modifiers: null);
        Assert.NotNull(method);
        return (string)method!.Invoke(null, [basePath])!;
    }

    private static PluginDescriptor CreateDescriptor()
        => new(
            "oop-test",
            "OutOfProcessTest",
            new Version(1, 0, 0),
            typeof(TestPlugin).FullName!,
            typeof(TestPlugin).Assembly.Location,
            new[] { PluginStage.Processing }.ToFrozenSet())
        {
            IsolationMode = PluginIsolationMode.OutOfProcess,
        };

    private static OutOfProcessPluginProxy CreateProxy(PluginHostClient client, MemoryMappedNotificationQueue queue, string assemblyPath, Action<PluginProcessNotification> publish)
        => new(
            new PluginDescriptor("oop-test", "OutOfProcessTest", new Version(1, 0, 0), typeof(TestPlugin).FullName!, assemblyPath, new[] { PluginStage.Processing }.ToFrozenSet()),
            client,
            queue,
            publish);

    private static void SetRuntimeState(OutOfProcessPluginRuntime runtime, string assemblyPath, OutOfProcessPluginProxy proxy)
    {
        var clients = GetField<ConcurrentDictionary<string, PluginHostClient>>(runtime, "_clients");
        var queues = GetField<ConcurrentDictionary<string, MemoryMappedNotificationQueue>>(runtime, "_notificationQueues");
        var proxies = GetField<ConcurrentDictionary<string, OutOfProcessPluginProxy>>(runtime, "_proxies");
        clients[assemblyPath] = proxy.Client;
        queues[assemblyPath] = GetField<MemoryMappedNotificationQueue>(proxy, "_notificationQueue");
        proxies[proxy.Id] = proxy;
    }

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

    private static MemoryMappedNotificationQueue InvokeGetNotificationQueue(OutOfProcessPluginRuntime runtime, string assemblyPath)
    {
        var method = typeof(OutOfProcessPluginRuntime).GetMethod("GetNotificationQueue", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (MemoryMappedNotificationQueue)method!.Invoke(runtime, [assemblyPath])!;
    }

    private static void InvokePublishQueuedNotifications(OutOfProcessPluginRuntime runtime, MemoryMappedNotificationQueue? queue)
    {
        var method = typeof(OutOfProcessPluginRuntime).GetMethod("PublishQueuedNotifications", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(runtime, [queue]);
    }

    private static void InvokePublishNotification(OutOfProcessPluginRuntime runtime, PluginProcessNotification notification)
    {
        var method = typeof(OutOfProcessPluginRuntime).GetMethod("PublishNotification", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(runtime, [notification]);
    }

    private static Exception InvokeCreateExceptionFromResponse(PluginHostResponse response)
    {
        var method = typeof(OutOfProcessPluginRuntime).GetMethod("CreateExceptionFromResponse", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Exception)method!.Invoke(null, [response])!;
    }

    private sealed class TestProcessCallback : IPluginProcessCallback
    {
        public List<PluginProcessNotification> Notifications { get; } = [];
        public bool ShutdownReceived { get; private set; }
        public string? UnloadedPluginId { get; private set; }
        public void OnNotification(PluginProcessNotification notification) => Notifications.Add(notification);
        public void OnUnloadCompleted(string pluginId) => UnloadedPluginId = pluginId;
        public void OnShutdownReceived() => ShutdownReceived = true;
    }

    private sealed class TestPlugin : IPlugin
    {
        public string Id => "oop-test";
        public string Name => "OutOfProcessTest";
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } = new[] { PluginStage.Processing }.ToFrozenSet();
        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default) => Task.FromResult<object?>("test-result");
    }
}
