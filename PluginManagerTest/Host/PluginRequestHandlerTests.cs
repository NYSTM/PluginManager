using System.Collections.Frozen;
using System.Text.Json;
using PluginHost;
using PluginManager;
using PluginManager.Ipc;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="PluginRequestHandler"/> のルーティングと入力検証テストです。
/// </summary>
public sealed class PluginRequestHandlerTests
{
    [Fact]
    public async Task HandleAsync_Ping_ReturnsSuccessResponse()
    {
        var handler = CreateHandler();
        var request = new PluginHostRequest
        {
            RequestId = "req-ping",
            Command = PluginHostCommand.Ping,
        };

        var response = await handler.HandleAsync(request, instanceIndex: 0, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("req-ping", response.RequestId);
        Assert.Null(response.ErrorType);
    }

    [Fact]
    public async Task HandleAsync_Shutdown_ReturnsSuccessResponse()
    {
        var handler = CreateHandler();
        var request = new PluginHostRequest
        {
            RequestId = "req-shutdown",
            Command = PluginHostCommand.Shutdown,
        };

        var response = await handler.HandleAsync(request, instanceIndex: 0, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("req-shutdown", response.RequestId);
    }

    [Fact]
    public async Task HandleAsync_UnknownCommand_ReturnsErrorResponse()
    {
        var handler = CreateHandler();
        var request = new PluginHostRequest
        {
            RequestId = "req-unknown",
            Command = (PluginHostCommand)999,
        };

        var response = await handler.HandleAsync(request, instanceIndex: 0, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal("req-unknown", response.RequestId);
        Assert.Equal(nameof(NotSupportedException), response.ErrorType);
        Assert.Contains("不明なコマンド", response.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_InitializeWithoutPluginId_ReturnsArgumentError()
    {
        var handler = CreateHandler();
        var request = new PluginHostRequest
        {
            RequestId = "req-init",
            Command = PluginHostCommand.Initialize,
        };

        var response = await handler.HandleAsync(request, instanceIndex: 0, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(nameof(ArgumentException), response.ErrorType);
        Assert.Contains("PluginId が必要", response.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_ExecuteWithoutStageId_ReturnsArgumentError()
    {
        var handler = CreateHandler();
        var request = new PluginHostRequest
        {
            RequestId = "req-exec",
            Command = PluginHostCommand.Execute,
            PluginId = "plugin-a",
        };

        var response = await handler.HandleAsync(request, instanceIndex: 0, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(nameof(ArgumentException), response.ErrorType);
        Assert.Contains("PluginId と StageId が必要", response.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_ExecuteWithUnloadedPlugin_ReturnsInvalidOperationError()
    {
        var handler = CreateHandler();
        var request = new PluginHostRequest
        {
            RequestId = "req-exec-unloaded",
            Command = PluginHostCommand.Execute,
            PluginId = "plugin-not-loaded",
            StageId = "Processing",
        };

        var response = await handler.HandleAsync(request, instanceIndex: 0, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(nameof(InvalidOperationException), response.ErrorType);
        Assert.Contains("ロードされていません", response.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_Load_WithInvalidRequest_ReturnsErrorResponse()
    {
        var handler = CreateHandler();
        var request = new PluginHostRequest
        {
            RequestId = "req-load-invalid",
            Command = PluginHostCommand.Load,
            PluginId = "plugin-load-invalid",
            AssemblyPath = typeof(PluginRequestHandlerTests).Assembly.Location,
            PluginTypeName = "",
        };

        var response = await handler.HandleAsync(request, instanceIndex: 0, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(nameof(ArgumentException), response.ErrorType);
        Assert.Contains("PluginId, AssemblyPath, PluginTypeName が必要", response.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_Unload_WithUnknownPlugin_ReturnsErrorResponse()
    {
        var handler = CreateHandler();
        var request = new PluginHostRequest
        {
            RequestId = "req-unload-missing",
            Command = PluginHostCommand.Unload,
            PluginId = "plugin-not-found",
        };

        var response = await handler.HandleAsync(request, instanceIndex: 0, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(nameof(InvalidOperationException), response.ErrorType);
        Assert.Contains("見つかりません", response.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_LoadAndUnload_ReturnsSuccessResponses()
    {
        using var registry = new PluginRegistry(new PluginHostNotifier());
        var handler = new PluginRequestHandler(registry, new PluginHostNotifier());

        var loadResponse = await handler.HandleAsync(CreateLoadRequest("plugin-load-unload"), instanceIndex: 0, CancellationToken.None);
        var unloadResponse = await handler.HandleAsync(new PluginHostRequest
        {
            RequestId = "req-unload-ok",
            Command = PluginHostCommand.Unload,
            PluginId = "plugin-load-unload",
        }, instanceIndex: 0, CancellationToken.None);

        Assert.True(loadResponse.Success);
        Assert.True(unloadResponse.Success);
    }

    [Fact]
    public async Task HandleAsync_Initialize_Success_ReturnsContextData()
    {
        using var registry = new PluginRegistry(new PluginHostNotifier());
        var handler = new PluginRequestHandler(registry, new PluginHostNotifier());
        await handler.HandleAsync(CreateLoadRequest("plugin-init-ok"), instanceIndex: 0, CancellationToken.None);

        var response = await handler.HandleAsync(new PluginHostRequest
        {
            RequestId = "req-init-ok",
            Command = PluginHostCommand.Initialize,
            PluginId = "plugin-init-ok",
            ContextData = new Dictionary<string, JsonElement>
            {
                ["initMode"] = JsonSerializer.SerializeToElement("ok"),
            },
        }, instanceIndex: 0, CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.ContextData);
        Assert.True(response.ContextData.ContainsKey("initialized"));
        Assert.True(response.ContextData["initialized"].GetBoolean());
    }

    [Fact]
    public async Task HandleAsync_Initialize_WhenPluginThrows_ReturnsErrorResponse()
    {
        using var registry = new PluginRegistry(new PluginHostNotifier());
        var handler = new PluginRequestHandler(registry, new PluginHostNotifier());
        await handler.HandleAsync(CreateLoadRequest("plugin-init-fail"), instanceIndex: 0, CancellationToken.None);

        var response = await handler.HandleAsync(new PluginHostRequest
        {
            RequestId = "req-init-fail",
            Command = PluginHostCommand.Initialize,
            PluginId = "plugin-init-fail",
            ContextData = new Dictionary<string, JsonElement>
            {
                ["initMode"] = JsonSerializer.SerializeToElement("throw"),
            },
        }, instanceIndex: 0, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(nameof(InvalidOperationException), response.ErrorType);
        Assert.Contains("初期化失敗", response.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_Execute_Success_ReturnsResultAndContext()
    {
        using var registry = new PluginRegistry(new PluginHostNotifier());
        var handler = new PluginRequestHandler(registry, new PluginHostNotifier());
        await handler.HandleAsync(CreateLoadRequest("plugin-exec-ok"), instanceIndex: 0, CancellationToken.None);

        var response = await handler.HandleAsync(new PluginHostRequest
        {
            RequestId = "req-exec-ok",
            Command = PluginHostCommand.Execute,
            PluginId = "plugin-exec-ok",
            StageId = "Processing",
            ContextData = new Dictionary<string, JsonElement>
            {
                ["execMode"] = JsonSerializer.SerializeToElement("ok"),
            },
        }, instanceIndex: 0, CancellationToken.None);

        Assert.True(response.Success);
        Assert.True(response.ResultData.HasValue);
        Assert.Equal("ok", response.ResultData.Value.GetProperty("status").GetString());
        Assert.NotNull(response.ContextData);
        Assert.True(response.ContextData.ContainsKey("executed"));
        Assert.True(response.ContextData["executed"].GetBoolean());
    }

    [Fact]
    public async Task HandleAsync_Execute_WhenPluginThrows_ReturnsErrorResponse()
    {
        using var registry = new PluginRegistry(new PluginHostNotifier());
        var handler = new PluginRequestHandler(registry, new PluginHostNotifier());
        await handler.HandleAsync(CreateLoadRequest("plugin-exec-fail"), instanceIndex: 0, CancellationToken.None);

        var response = await handler.HandleAsync(new PluginHostRequest
        {
            RequestId = "req-exec-fail",
            Command = PluginHostCommand.Execute,
            PluginId = "plugin-exec-fail",
            StageId = "Processing",
            ContextData = new Dictionary<string, JsonElement>
            {
                ["execMode"] = JsonSerializer.SerializeToElement("throw"),
            },
        }, instanceIndex: 0, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(nameof(InvalidOperationException), response.ErrorType);
        Assert.Contains("実行エラー", response.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_Execute_WhenCancellationRequested_ReturnsOperationCanceledError()
    {
        using var registry = new PluginRegistry(new PluginHostNotifier());
        var handler = new PluginRequestHandler(registry, new PluginHostNotifier());
        await handler.HandleAsync(CreateLoadRequest("plugin-exec-cancel"), instanceIndex: 0, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var response = await handler.HandleAsync(new PluginHostRequest
        {
            RequestId = "req-exec-cancel",
            Command = PluginHostCommand.Execute,
            PluginId = "plugin-exec-cancel",
            StageId = "Processing",
        }, instanceIndex: 0, cts.Token);

        Assert.False(response.Success);
        Assert.Equal(nameof(OperationCanceledException), response.ErrorType);
        Assert.Contains("キャンセル", response.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_Initialize_WhenCancellationRequested_ReturnsOperationCanceledError()
    {
        using var registry = new PluginRegistry(new PluginHostNotifier());
        var handler = new PluginRequestHandler(registry, new PluginHostNotifier());
        await handler.HandleAsync(CreateLoadRequest("plugin-init-cancel"), instanceIndex: 0, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var response = await handler.HandleAsync(new PluginHostRequest
        {
            RequestId = "req-init-cancel",
            Command = PluginHostCommand.Initialize,
            PluginId = "plugin-init-cancel",
        }, instanceIndex: 0, cts.Token);

        Assert.False(response.Success);
        Assert.Equal(nameof(OperationCanceledException), response.ErrorType);
        Assert.Contains("キャンセル", response.ErrorMessage);
    }

    private static PluginRequestHandler CreateHandler()
    {
        var notifier = new PluginHostNotifier();
        var registry = new PluginRegistry(notifier);
        return new PluginRequestHandler(registry, notifier);
    }

    private static PluginHostRequest CreateLoadRequest(string pluginId)
        => new()
        {
            RequestId = $"req-load-{pluginId}",
            Command = PluginHostCommand.Load,
            PluginId = pluginId,
            AssemblyPath = CreateIsolatedPluginAssemblyPath(),
            PluginTypeName = typeof(RequestHandlerLoadablePlugin).FullName!,
        };

    private static string CreateIsolatedPluginAssemblyPath()
    {
        var sourceAssemblyPath = typeof(PluginRequestHandlerTests).Assembly.Location;
        var tempDirectory = Path.Combine(Path.GetTempPath(), "PluginManagerTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        var copiedAssemblyPath = Path.Combine(tempDirectory, Path.GetFileName(sourceAssemblyPath));
        File.Copy(sourceAssemblyPath, copiedAssemblyPath, overwrite: true);
        return copiedAssemblyPath;
    }

    public sealed class RequestHandlerLoadablePlugin : IPlugin
    {
        public string Id => "request-handler-loadable";
        public string Name => "RequestHandlerLoadable";
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } =
            new[] { PluginStage.Processing }.ToFrozenSet();

        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
        {
            var mode = context.GetProperty<string>("initMode");
            if (string.Equals(mode, "throw", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("初期化失敗");

            cancellationToken.ThrowIfCancellationRequested();
            context.SetProperty("initialized", true);
            return Task.CompletedTask;
        }

        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var mode = context.GetProperty<string>("execMode");
            if (string.Equals(mode, "throw", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("実行失敗");

            context.SetProperty("executed", true);
            return Task.FromResult<object?>(new { status = "ok", stage = stage.Id });
        }
    }
}
