namespace PluginManager;

using System.Collections.Concurrent;
using System.Diagnostics;
using PluginManager.Ipc;

/// <summary>
/// 別プロセスでプラグインを実行するランタイムです。
/// </summary>
internal sealed class OutOfProcessPluginRuntime : IPluginRuntime, IDisposable
{
    private readonly ConcurrentDictionary<string, PluginHostClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MemoryMappedNotificationQueue> _notificationQueues = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, OutOfProcessPluginProxy> _proxies = new(StringComparer.OrdinalIgnoreCase);
    private readonly PluginProcessNotificationPublisher? _processNotificationPublisher;
    private readonly SemaphoreSlim _createLock = new(1, 1);
    private bool _disposed;

    public OutOfProcessPluginRuntime(PluginProcessNotificationPublisher? processNotificationPublisher = null)
    {
        _processNotificationPublisher = processNotificationPublisher;
    }

    public PluginIsolationMode IsolationMode => PluginIsolationMode.OutOfProcess;

    public async Task<PluginLoadResult> LoadAsync(
        PluginDescriptor descriptor,
        PluginContext context,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var client = await GetOrCreateClientAsync(descriptor.AssemblyPath, cancellationToken);
            var notificationQueue = GetNotificationQueue(descriptor.AssemblyPath);

            var loadRequest = new PluginHostRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Command = PluginHostCommand.Load,
                PluginId = descriptor.Id,
                AssemblyPath = descriptor.AssemblyPath,
                PluginTypeName = descriptor.PluginTypeName,
            };

            var loadResponse = await client.SendRequestAsync(loadRequest, cancellationToken);
            PublishQueuedNotifications(notificationQueue);
            if (!loadResponse.Success)
            {
                return new PluginLoadResult(
                    descriptor,
                    null,
                    CreateExceptionFromResponse(loadResponse));
            }

            var initRequest = new PluginHostRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Command = PluginHostCommand.Initialize,
                PluginId = descriptor.Id,
                ContextData = context.ToJsonDictionary(),
            };

            var initResponse = await client.SendRequestAsync(initRequest, cancellationToken);
            PublishQueuedNotifications(notificationQueue);
            if (!initResponse.Success)
            {
                return new PluginLoadResult(
                    descriptor,
                    null,
                    CreateExceptionFromResponse(initResponse));
            }

            if (initResponse.ContextData is not null)
                context.ApplyJsonDictionary(initResponse.ContextData);

            var proxy = new OutOfProcessPluginProxy(descriptor, client, notificationQueue, PublishNotification);
            _proxies[descriptor.Id] = proxy;

            return new PluginLoadResult(descriptor, proxy, null);
        }
        catch (Exception ex)
        {
            return new PluginLoadResult(descriptor, null, ex);
        }
    }

    public void Unload(string assemblyPath)
    {
        var toRemove = _proxies
            .Where(kv => kv.Value.Descriptor.AssemblyPath.Equals(assemblyPath, StringComparison.OrdinalIgnoreCase))
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

        _clients.TryRemove(assemblyPath, out var clientToDispose);
        _notificationQueues.TryRemove(assemblyPath, out var notificationQueue);

        foreach (var (id, proxy) in toRemove)
        {
            _proxies.TryRemove(id, out _);

            try
            {
                var request = new PluginHostRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Command = PluginHostCommand.Unload,
                    PluginId = id,
                };
                proxy.Client.SendRequestAsync(request, CancellationToken.None).GetAwaiter().GetResult();
                PublishQueuedNotifications(notificationQueue);
            }
            catch
            {
                // アンロード失敗は無視
            }
        }

        if (clientToDispose is not null)
        {
            try
            {
                var shutdownRequest = new PluginHostRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Command = PluginHostCommand.Shutdown,
                };
                clientToDispose.SendRequestAsync(shutdownRequest, CancellationToken.None).GetAwaiter().GetResult();
                PublishQueuedNotifications(notificationQueue);
            }
            catch
            {
                // シャットダウン失敗は無視
            }

            clientToDispose.Dispose();
        }

        notificationQueue?.Dispose();
    }

    public async Task UnloadAsync(string assemblyPath, CancellationToken cancellationToken = default)
    {
        var toRemove = _proxies
            .Where(kv => kv.Value.Descriptor.AssemblyPath.Equals(assemblyPath, StringComparison.OrdinalIgnoreCase))
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

        _clients.TryRemove(assemblyPath, out var clientToDispose);
        _notificationQueues.TryRemove(assemblyPath, out var notificationQueue);

        foreach (var (id, proxy) in toRemove)
        {
            _proxies.TryRemove(id, out _);

            try
            {
                var request = new PluginHostRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Command = PluginHostCommand.Unload,
                    PluginId = id,
                };
                await proxy.Client.SendRequestAsync(request, cancellationToken);
                PublishQueuedNotifications(notificationQueue);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // アンロード失敗は無視
            }
        }

        if (clientToDispose is not null)
        {
            try
            {
                var shutdownRequest = new PluginHostRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Command = PluginHostCommand.Shutdown,
                };
                await clientToDispose.SendRequestAsync(shutdownRequest, cancellationToken);
                PublishQueuedNotifications(notificationQueue);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // シャットダウン失敗は無視
            }

            clientToDispose.Dispose();
        }

        notificationQueue?.Dispose();
    }

    public void UnloadAll()
    {
        var clients = _clients.Values.ToList();
        var notificationQueues = _notificationQueues.Values.ToList();
        _proxies.Clear();
        _clients.Clear();
        _notificationQueues.Clear();

        foreach (var client in clients)
        {
            try
            {
                var shutdownRequest = new PluginHostRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Command = PluginHostCommand.Shutdown,
                };
                client.SendRequestAsync(shutdownRequest, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
                // シャットダウン失敗は無視
            }

            client.Dispose();
        }

        foreach (var notificationQueue in notificationQueues)
        {
            PublishQueuedNotifications(notificationQueue);
            notificationQueue.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        UnloadAll();
        _createLock.Dispose();
    }

    private async Task<PluginHostClient> GetOrCreateClientAsync(string assemblyPath, CancellationToken cancellationToken)
    {
        if (_clients.TryGetValue(assemblyPath, out var existing) && existing.IsConnected)
            return existing;

        await _createLock.WaitAsync(cancellationToken);
        try
        {
            if (_clients.TryGetValue(assemblyPath, out var existing2) && existing2.IsConnected)
                return existing2;

            var pipeName = $"PluginHost_{Guid.NewGuid():N}";
            var notificationQueueName = $"PluginHostNotify_{Guid.NewGuid():N}";
            var hostPath = FindPluginHostExecutable();
            var notificationQueue = new MemoryMappedNotificationQueue(notificationQueueName);

            var startInfo = new ProcessStartInfo
            {
                FileName = hostPath,
                Arguments = $"{pipeName} {notificationQueueName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            Process? process = null;
            PluginHostClient? client = null;

            try
            {
                process = Process.Start(startInfo) ?? throw new InvalidOperationException("PluginHost プロセスの起動に失敗しました。");
                client = new PluginHostClient(pipeName, process);

                await client.ConnectAsync(5000, cancellationToken);

                var pingRequest = new PluginHostRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Command = PluginHostCommand.Ping,
                };
                var pingResponse = await client.SendRequestAsync(pingRequest, cancellationToken);
                if (!pingResponse.Success)
                    throw new InvalidOperationException("PluginHost プロセスが応答しません。");

                _clients[assemblyPath] = client;
                _notificationQueues[assemblyPath] = notificationQueue;
                PublishQueuedNotifications(notificationQueue);

                return client;
            }
            catch
            {
                client?.Dispose();
                notificationQueue.Dispose();
                if (process is not null && !process.HasExited)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(1000);
                    }
                    catch
                    {
                        // プロセス終了失敗は無視
                    }
                }
                process?.Dispose();
                throw;
            }
        }
        finally
        {
            _createLock.Release();
        }
    }

    private MemoryMappedNotificationQueue GetNotificationQueue(string assemblyPath)
        => _notificationQueues[assemblyPath];

    private void PublishQueuedNotifications(MemoryMappedNotificationQueue? notificationQueue)
    {
        if (notificationQueue is null)
            return;

        foreach (var notification in notificationQueue.Drain())
            PublishNotification(notification);
    }

    private void PublishNotification(PluginProcessNotification notification)
        => _processNotificationPublisher?.Publish(notification);

    private static string FindPluginHostExecutable()
        => FindPluginHostExecutable(AppContext.BaseDirectory);

    private static string FindPluginHostExecutable(string basePath)
    {
        var hostPath = Path.Combine(basePath, "PluginHost.exe");

        if (File.Exists(hostPath))
            return hostPath;

        hostPath = Path.Combine(basePath, "..", "PluginHost", "PluginHost.exe");
        if (File.Exists(Path.GetFullPath(hostPath)))
            return Path.GetFullPath(hostPath);

        throw new FileNotFoundException("PluginHost.exe が見つかりません。");
    }

    private static Exception CreateExceptionFromResponse(PluginHostResponse response)
    {
        var message = response.ErrorMessage ?? "不明なエラー";
        return response.ErrorType switch
        {
            nameof(InvalidOperationException) => new InvalidOperationException(message),
            nameof(ArgumentException) => new ArgumentException(message),
            nameof(NotSupportedException) => new NotSupportedException(message),
            _ => new Exception(message),
        };
    }
}
