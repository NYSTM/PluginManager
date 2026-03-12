namespace PluginManager;

using System.Diagnostics;
using PluginManager.Ipc;

/// <summary>
/// 別プロセスでプラグインを実行するランタイムです。
/// </summary>
internal sealed class OutOfProcessPluginRuntime : IPluginRuntime, IDisposable
{
    private readonly Dictionary<string, PluginHostClient> _clients = new();
    private readonly Dictionary<string, OutOfProcessPluginProxy> _proxies = new();
    private readonly object _lock = new();
    private readonly SemaphoreSlim _createLock = new(1, 1);
    private bool _disposed;

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

            var loadRequest = new PluginHostRequest
            {
                RequestId = Guid.NewGuid().ToString(),
                Command = PluginHostCommand.Load,
                PluginId = descriptor.Id,
                AssemblyPath = descriptor.AssemblyPath,
                PluginTypeName = descriptor.PluginTypeName,
            };

            var loadResponse = await client.SendRequestAsync(loadRequest, cancellationToken);
            if (!loadResponse.Success)
            {
                return new PluginLoadResult(
                    descriptor,
                    null,
                    CreateExceptionFromResponse(loadResponse));
            }

            var initRequest = new PluginHostRequest
            {
                RequestId = Guid.NewGuid().ToString(),
                Command = PluginHostCommand.Initialize,
                PluginId = descriptor.Id,
                ContextData = context.ToJsonDictionary(),
            };

            var initResponse = await client.SendRequestAsync(initRequest, cancellationToken);
            if (!initResponse.Success)
            {
                return new PluginLoadResult(
                    descriptor,
                    null,
                    CreateExceptionFromResponse(initResponse));
            }

            if (initResponse.ContextData is not null)
                context.ApplyJsonDictionary(initResponse.ContextData);

            var proxy = new OutOfProcessPluginProxy(descriptor, client);
            lock (_lock)
            {
                _proxies[descriptor.Id] = proxy;
            }

            return new PluginLoadResult(descriptor, proxy, null);
        }
        catch (Exception ex)
        {
            return new PluginLoadResult(descriptor, null, ex);
        }
    }

    public void Unload(string assemblyPath)
    {
        List<(string id, OutOfProcessPluginProxy proxy)> toRemove;
        PluginHostClient? clientToDispose = null;

        lock (_lock)
        {
            toRemove = _proxies
                .Where(kv => kv.Value.Descriptor.AssemblyPath.Equals(assemblyPath, StringComparison.OrdinalIgnoreCase))
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

            foreach (var (id, _) in toRemove)
                _proxies.Remove(id);

            if (_clients.Remove(assemblyPath, out var client))
                clientToDispose = client;
        }

        foreach (var (id, proxy) in toRemove)
        {
            try
            {
                var request = new PluginHostRequest
                {
                    RequestId = Guid.NewGuid().ToString(),
                    Command = PluginHostCommand.Unload,
                    PluginId = id,
                };
                proxy.Client.SendRequestAsync(request, CancellationToken.None).GetAwaiter().GetResult();
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
                    RequestId = Guid.NewGuid().ToString(),
                    Command = PluginHostCommand.Shutdown,
                };
                clientToDispose.SendRequestAsync(shutdownRequest, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
                // シャットダウン失敗は無視
            }

            clientToDispose.Dispose();
        }
    }

    public async Task UnloadAsync(string assemblyPath, CancellationToken cancellationToken = default)
    {
        List<(string id, OutOfProcessPluginProxy proxy)> toRemove;
        PluginHostClient? clientToDispose = null;

        lock (_lock)
        {
            toRemove = _proxies.Where(kv => kv.Value.Descriptor.AssemblyPath.Equals(assemblyPath, StringComparison.OrdinalIgnoreCase))
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

            foreach (var (id, _) in toRemove)
                _proxies.Remove(id);

            if (_clients.Remove(assemblyPath, out var client))
                clientToDispose = client;
        }

        foreach (var (id, proxy) in toRemove)
        {
            try
            {
                var request = new PluginHostRequest
                {
                    RequestId = Guid.NewGuid().ToString(),
                    Command = PluginHostCommand.Unload,
                    PluginId = id,
                };
                await proxy.Client.SendRequestAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;  // キャンセルは再スロー
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
                    RequestId = Guid.NewGuid().ToString(),
                    Command = PluginHostCommand.Shutdown,
                };
                await clientToDispose.SendRequestAsync(shutdownRequest, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;  // キャンセルは再スロー
            }
            catch
            {
                // シャットダウン失敗は無視
            }

            clientToDispose.Dispose();
        }
    }

    public void UnloadAll()
    {
        List<PluginHostClient> clients;

        lock (_lock)
        {
            _proxies.Clear();
            clients = [.. _clients.Values];
            _clients.Clear();
        }

        foreach (var client in clients)
        {
            try
            {
                var shutdownRequest = new PluginHostRequest
                {
                    RequestId = Guid.NewGuid().ToString(),
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
        lock (_lock)
        {
            if (_clients.TryGetValue(assemblyPath, out var existing) && existing.IsConnected)
                return existing;
        }

        await _createLock.WaitAsync(cancellationToken);
        try
        {
            // セマフォ取得後に再チェック（別スレッドが既に作成済みの可能性）
            lock (_lock)
            {
                if (_clients.TryGetValue(assemblyPath, out var existing) && existing.IsConnected)
                    return existing;
            }

            var pipeName = $"PluginHost_{Guid.NewGuid():N}";
            var hostPath = FindPluginHostExecutable();

            var startInfo = new ProcessStartInfo
            {
                FileName = hostPath,
                Arguments = pipeName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            var process = Process.Start(startInfo) ?? throw new InvalidOperationException("PluginHost プロセスの起動に失敗しました。");
            var client = new PluginHostClient(pipeName, process);

            await client.ConnectAsync(5000, cancellationToken);

            var pingRequest = new PluginHostRequest
            {
                RequestId = Guid.NewGuid().ToString(),
                Command = PluginHostCommand.Ping,
            };
            var pingResponse = await client.SendRequestAsync(pingRequest, cancellationToken);
            if (!pingResponse.Success)
                throw new InvalidOperationException("PluginHost プロセスが応答しません。");

            lock (_lock)
            {
                _clients[assemblyPath] = client;
            }

            return client;
        }
        finally
        {
            _createLock.Release();
        }
    }

    private static string FindPluginHostExecutable()
    {
        var basePath = AppContext.BaseDirectory;
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

/// <summary>
/// 別プロセスで実行されるプラグインのプロキシです。
/// </summary>
internal sealed class OutOfProcessPluginProxy : IPlugin
{
    public PluginDescriptor Descriptor { get; }
    public PluginHostClient Client { get; }

    public OutOfProcessPluginProxy(PluginDescriptor descriptor, PluginHostClient client)
    {
        Descriptor = descriptor;
        Client = client;
    }

    public string Id => Descriptor.Id;
    public string Name => Descriptor.Name;
    public Version Version => Descriptor.Version;
    public IReadOnlySet<PluginStage> SupportedStages => Descriptor.SupportedStages;

    public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default)
    {
        var request = new PluginHostRequest
        {
            RequestId = Guid.NewGuid().ToString(),
            Command = PluginHostCommand.Execute,
            PluginId = Id,
            StageId = stage.Id,
            ContextData = context.ToJsonDictionary(),
        };

        var response = await Client.SendRequestAsync(request, cancellationToken);

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
}
