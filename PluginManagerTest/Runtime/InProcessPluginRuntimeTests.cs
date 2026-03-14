using System.Collections.Frozen;
using System.Reflection;
using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="InProcessPluginRuntime"/> のテストです。
/// </summary>
public sealed class InProcessPluginRuntimeTests
{
    [Fact]
    public void IsolationMode_ReturnsInProcess()
    {
        var runtime = new InProcessPluginRuntime();
        Assert.Equal(PluginIsolationMode.InProcess, runtime.IsolationMode);
    }

    [Fact]
    public async Task LoadAsync_WithValidPlugin_ReturnsSuccess()
    {
        var runtime = new InProcessPluginRuntime();
        var context = new PluginContext();
        var descriptor = CreateDescriptor(typeof(InProcessRuntimeSuccessfulPlugin));

        var result = await runtime.LoadAsync(descriptor, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Instance);
        Assert.True(context.TryGetProperty<string>(InProcessRuntimeSuccessfulPlugin.InitializedKey, out var value));
        Assert.Equal(InProcessRuntimeSuccessfulPlugin.InitializedValue, value);

        runtime.UnloadAll();
    }

    [Fact]
    public async Task LoadAsync_WithInvalidPluginType_ReturnsInvalidOperationException()
    {
        var runtime = new InProcessPluginRuntime();
        var descriptor = CreateDescriptor(typeof(InProcessPluginRuntimeTests));

        var result = await runtime.LoadAsync(descriptor, new PluginContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Equal(0, GetLoadContextCount(runtime));
    }

    [Fact]
    public async Task LoadAsync_WhenInitializeThrows_ReturnsErrorAndRemovesContext()
    {
        var runtime = new InProcessPluginRuntime();
        var descriptor = CreateDescriptor(typeof(InProcessRuntimeFailingInitializePlugin));

        var result = await runtime.LoadAsync(descriptor, new PluginContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Equal(0, GetLoadContextCount(runtime));
    }

    [Fact]
    public void Unload_NonExistentAssembly_DoesNotThrow()
    {
        var runtime = new InProcessPluginRuntime();
        var ex = Record.Exception(() => runtime.Unload("missing.dll"));
        Assert.Null(ex);
    }

    [Fact]
    public async Task UnloadAsync_NonExistentAssembly_DoesNotThrow()
    {
        var runtime = new InProcessPluginRuntime();
        var ex = await Record.ExceptionAsync(() => runtime.UnloadAsync("missing.dll"));
        Assert.Null(ex);
    }

    [Fact]
    public async Task Unload_AfterLoad_RemovesContext()
    {
        var runtime = new InProcessPluginRuntime();
        var descriptor = CreateDescriptor(typeof(InProcessRuntimeSuccessfulPlugin));
        var loadResult = await runtime.LoadAsync(descriptor, new PluginContext(), CancellationToken.None);

        Assert.True(loadResult.Success);
        Assert.Equal(1, GetLoadContextCount(runtime));

        runtime.Unload(descriptor.AssemblyPath);

        Assert.Equal(0, GetLoadContextCount(runtime));
    }

    [Fact]
    public void RemoveContext_WithDifferentLoadContext_KeepsStoredContext()
    {
        var runtime = new InProcessPluginRuntime();
        var assemblyPath = typeof(InProcessRuntimeSuccessfulPlugin).Assembly.Location;
        var storedContext = new PluginLoadContext(assemblyPath);
        var otherContext = new PluginLoadContext(assemblyPath);
        SetLoadContext(runtime, assemblyPath, storedContext);

        InvokeRemoveContext(runtime, assemblyPath, otherContext);

        Assert.Equal(1, GetLoadContextCount(runtime));

        storedContext.Unload();
    }

    [Fact]
    public void RemoveContext_WithNullLoadContext_KeepsStoredContext()
    {
        var runtime = new InProcessPluginRuntime();
        var assemblyPath = typeof(InProcessRuntimeSuccessfulPlugin).Assembly.Location;
        var storedContext = new PluginLoadContext(assemblyPath);
        SetLoadContext(runtime, assemblyPath, storedContext);

        InvokeRemoveContext(runtime, assemblyPath, null);

        Assert.Equal(1, GetLoadContextCount(runtime));

        storedContext.Unload();
    }

    [Fact]
    public async Task UnloadAll_AfterLoad_ClearsContexts()
    {
        var runtime = new InProcessPluginRuntime();
        var descriptor = CreateDescriptor(typeof(InProcessRuntimeSuccessfulPlugin));

        var loadResult = await runtime.LoadAsync(descriptor, new PluginContext(), CancellationToken.None);

        Assert.True(loadResult.Success);
        Assert.Equal(1, GetLoadContextCount(runtime));

        runtime.UnloadAll();

        Assert.Equal(0, GetLoadContextCount(runtime));
    }

    private static PluginDescriptor CreateDescriptor(Type pluginType)
        => new(
            pluginType.FullName!,
            pluginType.Name,
            new Version(1, 0, 0),
            pluginType.FullName!,
            pluginType.Assembly.Location,
            new[] { PluginStage.Processing }.ToFrozenSet());

    private static void SetLoadContext(InProcessPluginRuntime runtime, string assemblyPath, PluginLoadContext context)
    {
        var field = typeof(InProcessPluginRuntime).GetField("_loadContexts", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var contexts = field!.GetValue(runtime)!;
        var indexer = contexts.GetType().GetProperty("Item");
        Assert.NotNull(indexer);
        indexer!.SetValue(contexts, context, [assemblyPath]);
    }

    private static void InvokeRemoveContext(InProcessPluginRuntime runtime, string assemblyPath, PluginLoadContext? loadContext)
    {
        var method = typeof(InProcessPluginRuntime).GetMethod("RemoveContext", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(string), typeof(PluginLoadContext)]);
        Assert.NotNull(method);
        method!.Invoke(runtime, [assemblyPath, loadContext]);
    }

    private static int GetLoadContextCount(InProcessPluginRuntime runtime)
    {
        var field = typeof(InProcessPluginRuntime).GetField("_loadContexts", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var contexts = field!.GetValue(runtime);
        var countProperty = contexts!.GetType().GetProperty("Count");
        Assert.NotNull(countProperty);
        return (int)countProperty!.GetValue(contexts)!;
    }
}
