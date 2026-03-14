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
        var result = await proxy.ExecuteAsync(PluginStage.Processing, context);

        var resultElement = Assert.IsType<JsonElement>(result);
        Assert.Equal("ok-result", resultElement.GetString());
        Assert.True(context.TryGetProperty<string>("updated", out var updated));
        Assert.Equal("done", updated);
        Assert.Single(published);
        Assert.Equal(PluginProcessNotificationType.ExecuteCompleted, published[0].NotificationType);
        await serverTask;
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
            var request = await ReadRequestAsync(server);
            var response = new PluginHostResponse
            {
                RequestId = request.RequestId,
                Success = false,
                ErrorType = errorType,
                ErrorMessage = errorMessage,
            };

            await WriteResponseAsync(server, response);
        });

        using var client = new PluginHostClient(pipeName);
        using var queue = new MemoryMappedNotificationQueue(queueName);
        await client.ConnectAsync();
        var proxy = new OutOfProcessPluginProxy(CreateDescriptor(), client, queue, _ => { });

        var ex = await Assert.ThrowsAsync(expectedType, () =>
            proxy.ExecuteAsync(PluginStage.Processing, new PluginContext()));

        Assert.Equal(expectedMessage, ex.Message);
        await serverTask;
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
    }

    private static async Task<PluginHostRequest> ReadRequestAsync(NamedPipeServerStream server)
    {
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        var line = await reader.ReadLineAsync();
        Assert.False(string.IsNullOrWhiteSpace(line));
        return JsonSerializer.Deserialize<PluginHostRequest>(line!)!;
    }

    private static async Task WriteResponseAsync(NamedPipeServerStream server, PluginHostResponse response)
    {
        using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true,
        };

        await writer.WriteLineAsync(JsonSerializer.Serialize(response));
    }
}
