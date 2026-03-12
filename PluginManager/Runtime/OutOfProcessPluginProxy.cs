using PluginManager.Ipc;

namespace PluginManager;

/// <summary>
/// 別プロセスで実行されるプラグインのプロキシです。
/// </summary>
internal sealed class OutOfProcessPluginProxy : IPlugin
{
    private readonly Action<PluginProcessNotification> _publishNotification;
    private readonly MemoryMappedNotificationQueue _notificationQueue;

    public OutOfProcessPluginProxy(
        PluginDescriptor descriptor,
        PluginHostClient client,
        MemoryMappedNotificationQueue notificationQueue,
        Action<PluginProcessNotification> publishNotification)
    {
        Descriptor = descriptor;
        Client = client;
        _notificationQueue = notificationQueue;
        _publishNotification = publishNotification;
    }

    public PluginDescriptor Descriptor { get; }
    public PluginHostClient Client { get; }

    public string Id => Descriptor.Id;
    public string Name => Descriptor.Name;
    public Version Version => Descriptor.Version;
    public IReadOnlySet<PluginStage> SupportedStages => Descriptor.SupportedStages;

    public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
    {
        var request = new PluginHostRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Command = PluginHostCommand.Execute,
            PluginId = Id,
            StageId = stage.Id,
            ContextData = context.ToJsonDictionary(),
        };

        var response = await Client.SendRequestAsync(request, cancellationToken);
        PublishQueuedNotifications();

        if (!response.Success)
        {
            var message = response.ErrorMessage ?? "プラグイン実行エラー";
            throw response.ErrorType switch
            {
                nameof(InvalidOperationException) => new InvalidOperationException(message),
                nameof(ArgumentException) => new ArgumentException(message),
                _ => new Exception(message),
            };
        }

        if (response.ContextData is not null)
            context.ApplyJsonDictionary(response.ContextData);

        return response.ResultData;
    }

    private void PublishQueuedNotifications()
    {
        foreach (var notification in _notificationQueue.Drain())
            _publishNotification(notification);
    }
}
