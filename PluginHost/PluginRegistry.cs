using System.Collections.Concurrent;
using PluginManager;

namespace PluginHost;

/// <summary>
/// ロード済みプラグインとそのロードコンテキストを管理します。
/// </summary>
internal sealed class PluginRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, IPlugin> _plugins = new();
    private readonly ConcurrentDictionary<string, PluginLoadContext> _contexts = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// プラグインをロードして登録します。
    /// </summary>
    public PluginHostResponse Load(PluginHostRequest request, int instanceIndex)
    {
        if (string.IsNullOrEmpty(request.PluginId) || string.IsNullOrEmpty(request.AssemblyPath) || string.IsNullOrEmpty(request.PluginTypeName))
        {
            return ErrorResponse(request.RequestId, "PluginId, AssemblyPath, PluginTypeName が必要です。", nameof(ArgumentException));
        }

        lock (_lock)
        {
            if (_plugins.ContainsKey(request.PluginId))
                return ErrorResponse(request.RequestId, $"プラグイン '{request.PluginId}' は既にロードされています。", nameof(InvalidOperationException));

            var loadContext = new PluginLoadContext(request.AssemblyPath);
            try
            {
                var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(request.AssemblyPath));
                var type = assembly.GetType(request.PluginTypeName);

                if (type is null || Activator.CreateInstance(type) is not IPlugin plugin)
                {
                    loadContext.Unload();
                    return ErrorResponse(request.RequestId, $"型 '{request.PluginTypeName}' が見つからないか、IPlugin を実装していません。", nameof(InvalidOperationException));
                }

                _plugins[request.PluginId] = plugin;
                _contexts[request.PluginId] = loadContext;

                Console.WriteLine($"[PluginHost#{instanceIndex}] ロード完了: {request.PluginId}");
                return new PluginHostResponse { RequestId = request.RequestId, Success = true };
            }
            catch (Exception ex)
            {
                loadContext.Unload();
                return ErrorResponse(request.RequestId, $"プラグインロードエラー: {ex.Message}", ex.GetType().Name);
            }
        }
    }

    /// <summary>
    /// プラグインをアンロードして登録から削除します。
    /// </summary>
    public PluginHostResponse Unload(PluginHostRequest request, int instanceIndex)
    {
        if (string.IsNullOrEmpty(request.PluginId))
            return ErrorResponse(request.RequestId, "PluginId が必要です。", nameof(ArgumentException));

        lock (_lock)
        {
            var pluginRemoved = _plugins.TryRemove(request.PluginId, out _);
            var contextRemoved = _contexts.TryRemove(request.PluginId, out var ctx);

            if (!pluginRemoved && !contextRemoved)
                return ErrorResponse(request.RequestId, $"プラグイン '{request.PluginId}' が見つかりません。", nameof(InvalidOperationException));

            if (ctx is not null)
            {
                ctx.Unload();
                GC.Collect(0, GCCollectionMode.Optimized);
            }

            Console.WriteLine($"[PluginHost#{instanceIndex}] アンロード完了: {request.PluginId}");
            return new PluginHostResponse { RequestId = request.RequestId, Success = true };
        }
    }

    /// <summary>
    /// 指定 ID のプラグインを取得します。
    /// </summary>
    public bool TryGet(string pluginId, out IPlugin plugin)
        => _plugins.TryGetValue(pluginId, out plugin!);

    /// <summary>
    /// 全プラグインをアンロードしてリソースを解放します。
    /// </summary>
    public void UnloadAll()
    {
        lock (_lock)
        {
            Console.WriteLine("[PluginHost] 全プラグインアンロード開始");
            _plugins.Clear();

            foreach (var ctx in _contexts.Values)
                ctx.Unload();

            _contexts.Clear();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Console.WriteLine("[PluginHost] 全プラグインアンロード完了");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        UnloadAll();
    }

    private static PluginHostResponse ErrorResponse(string requestId, string message, string errorType)
        => new() { RequestId = requestId, Success = false, ErrorMessage = message, ErrorType = errorType };
}
