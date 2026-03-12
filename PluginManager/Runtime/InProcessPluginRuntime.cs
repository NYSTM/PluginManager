using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PluginManager;

internal sealed class InProcessPluginRuntime : IPluginRuntime
{
    private readonly ConcurrentDictionary<string, PluginLoadContext> _loadContexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly ILogger? _logger;
    private readonly UnloadVerifier _unloadVerifier;

    public PluginIsolationMode IsolationMode => PluginIsolationMode.InProcess;

    public InProcessPluginRuntime(ILogger? logger = null)
    {
        _logger = logger;
        _unloadVerifier = new UnloadVerifier(logger);
    }

    public async Task<PluginLoadResult> LoadAsync(
        PluginDescriptor descriptor,
        PluginContext context,
        CancellationToken cancellationToken)
    {
        PluginLoadContext? loadContext = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            loadContext = new PluginLoadContext(descriptor.AssemblyPath);

            lock (_lock)
            {
                if (_loadContexts.TryGetValue(descriptor.AssemblyPath, out var old))
                    old.Unload();
                _loadContexts[descriptor.AssemblyPath] = loadContext;
            }

            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(descriptor.AssemblyPath));
            var type = assembly.GetType(descriptor.PluginTypeName);
            if (type is null || Activator.CreateInstance(type) is not IPlugin plugin)
            {
                RemoveContext(descriptor.AssemblyPath, loadContext);
                return new PluginLoadResult(
                    descriptor,
                    null,
                    new InvalidOperationException($"型 '{descriptor.PluginTypeName}' は有効なプラグインではありません。"));
            }

            cancellationToken.ThrowIfCancellationRequested();
            await plugin.InitializeAsync(context, cancellationToken);
            return new PluginLoadResult(descriptor, plugin, null);
        }
        catch (Exception ex)
        {
            RemoveContext(descriptor.AssemblyPath, loadContext);
            return new PluginLoadResult(descriptor, null, ex);
        }
    }

    public void Unload(string assemblyPath)
    {
        var context = RemoveContext(assemblyPath);
        if (context is null)
            return;

        context.Unload();
        _ = Task.Run(ForceCollect);
    }

    public async Task UnloadAsync(string assemblyPath, CancellationToken cancellationToken = default)
    {
        var context = RemoveContext(assemblyPath);
        if (context is null)
            return;

        // アンロード検証を実行
        var isUnloaded = await _unloadVerifier.VerifyUnloadAsync(
            context,
            timeout: TimeSpan.FromSeconds(10),
            cancellationToken);

        if (!isUnloaded)
        {
            _logger?.LogWarning(
                "プラグインのアンロードに失敗しました: {AssemblyPath}。メモリリークの可能性があります。",
                assemblyPath);
        }
    }

    public void UnloadAll()
    {
        List<PluginLoadContext> contexts;
        lock (_lock)
        {
            contexts = [.. _loadContexts.Values];
            _loadContexts.Clear();
        }

        foreach (var context in contexts)
            context.Unload();
    }

    private void RemoveContext(string assemblyPath, PluginLoadContext? loadContext)
    {
        lock (_lock)
        {
            if (loadContext is not null &&
                _loadContexts.TryGetValue(assemblyPath, out var current) &&
                ReferenceEquals(current, loadContext))
            {
                _loadContexts.TryRemove(assemblyPath, out _);
            }
        }

        loadContext?.Unload();
    }

    private PluginLoadContext? RemoveContext(string assemblyPath)
    {
        lock (_lock)
        {
            if (_loadContexts.TryRemove(assemblyPath, out var context))
            {
                return context;
            }

            return null;
        }
    }

    private static void ForceCollect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
