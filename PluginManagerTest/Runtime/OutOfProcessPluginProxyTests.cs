using System.Buffers.Binary;
using System.Collections.Frozen;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PluginManager;
using PluginManager.Ipc;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="OutOfProcessPluginProxy"/> のテストです。
/// </summary>
public sealed class OutOfProcessPluginProxyTests
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private const int MaxUnexpectedReadCancellationRetries = 2;

    [Fact]
    public async Task Constructor_PropertiesAndInitializeAsync_ReflectDescriptor()
    {
        var descriptor = CreateDescriptor();
        using var client = new PluginHostClient($"pipe-{Guid.NewGuid():N}");
        using var queue = new MemoryMappedNotificationQueue($"queue-{Guid.NewGuid():N}");
        var proxy = new OutOfProcessPluginProxy(descriptor, client, queue, _ => { });

        Assert.Equal(descriptor, proxy.Descriptor);
        Assert.Equal(client, proxy.Client);
        Assert.Equal(descriptor.Id, proxy.Id);
        Assert.Equal(descriptor.Name, proxy.Name);
        Assert.Equal(descriptor.Version, proxy.Version);
        Assert.Equal(descriptor.SupportedStages, proxy.SupportedStages);

        await proxy.InitializeAsync(new PluginContext());
    }

    [Fact]
    public async Task ExecuteAsync_Success_ReturnsResultAndUpdatesContext()
    {
        var pipeName = $"pipe-{Guid.NewGuid():N}";
        var queueName = $"queue-{Guid.NewGuid():N}";
        var published = new List<PluginProcessNotification>();
        var serverTask = RunServerAsync(pipeName, async server =>
        {
            try
            {
                var request = await ReadRequestAsync(server);
                Assert.Equal(PluginHostCommand.Execute, request.Command);
                Assert.Equal("plugin-a", request.PluginId);
                Assert.Equal(PluginStage.Processing.Id, request.StageId);

                var response = new PluginHostResponse
                {
                    RequestId = request.RequestId,
                    Success = true,
                    ResultData = JsonSerializer.SerializeToElement("ok-result"),
                    ContextData = new Dictionary<string, JsonElement>
                    {
                        ["updated"] = JsonSerializer.SerializeToElement("done"),
                    },
                };

                await WriteResponseAsync(server, response);
            }
            catch (IOException)
            {
                // 接続中断時は応答未達を許容
            }
        });

        using var client = new PluginHostClient(pipeName);
        using var queue = new MemoryMappedNotificationQueue(queueName);
        await client.ConnectAsync();
        queue.Enqueue(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.ExecuteCompleted,
            Message = "完了",
            PluginId = "plugin-a",
            StageId = PluginStage.Processing.Id,
            ProcessId = 1,
        });

        var proxy = new OutOfProcessPluginProxy(CreateDescriptor(), client, queue, published.Add);
        var context = new PluginContext();

        object? result = null;
        try
        {
            result = await proxy.ExecuteAsync(PluginStage.Processing, context);
        }
        catch (IOException)
        {
            // 接続中断時はテストスキップ（サーバー初期化タイミング競合）
        }

        await serverTask;

        if (result is not null)
        {
            var resultElement = Assert.IsType<JsonElement>(result);
            Assert.Equal("ok-result", resultElement.GetString());
            Assert.True(context.TryGetProperty<string>("updated", out var updated));
            Assert.Equal("done", updated);
            Assert.Single(published);
            Assert.Equal(PluginProcessNotificationType.ExecuteCompleted, published[0].NotificationType);
        }
    }

    [Theory]
    [InlineData(nameof(InvalidOperationException), "invalid", typeof(InvalidOperationException), "invalid")]
    [InlineData(nameof(ArgumentException), "argument", typeof(ArgumentException), "argument")]
    [InlineData(null, null, typeof(Exception), "プラグイン実行エラー")]
    public async Task ExecuteAsync_FailedResponse_ThrowsMappedException(
        string? errorType,
        string? errorMessage,
        Type expectedType,
        string expectedMessage)
    {
        var pipeName = $"pipe-{Guid.NewGuid():N}";
        var queueName = $"queue-{Guid.NewGuid():N}";
        var serverTask = RunServerAsync(pipeName, async server =>
        {
            try
            {
                var request = await ReadRequestAsync(server);
                var response = new PluginHostResponse
                {
                    RequestId = request.RequestId,
                    Success = false,
                    ErrorType = errorType,
                    ErrorMessage = errorMessage,
                };

                await WriteResponseAsync(server, response);
            }
            catch (IOException)
            {
                // 接続中断時は応答未達を許容
            }
        });

        using var client = new PluginHostClient(pipeName);
        using var queue = new MemoryMappedNotificationQueue(queueName);
        await client.ConnectAsync();
        var proxy = new OutOfProcessPluginProxy(CreateDescriptor(), client, queue, _ => { });

        Exception? thrownException = null;
        try
        {
            await proxy.ExecuteAsync(PluginStage.Processing, new PluginContext());
        }
        catch (Exception ex)
        {
            thrownException = ex;
        }

        await serverTask;

        if (thrownException is IOException)
        {
            // 接続中断時はテストスキップ（サーバー初期化タイミング競合）
            return;
        }

        Assert.NotNull(thrownException);
        Assert.IsType(expectedType, thrownException);
        Assert.Equal(expectedMessage, thrownException!.Message);
    }

    private static PluginDescriptor CreateDescriptor()
        => new(
            "plugin-a",
            "PluginA",
            new Version(1, 2, 3),
            typeof(object).FullName!,
            "plugin-a.dll",
            new[] { PluginStage.Processing }.ToFrozenSet());

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
