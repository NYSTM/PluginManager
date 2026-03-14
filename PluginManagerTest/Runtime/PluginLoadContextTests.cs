using System.Reflection;
using System.Runtime.Loader;
using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="PluginLoadContext"/> のテストです。
/// </summary>
public sealed class PluginLoadContextTests
{
    [Fact]
    public void Load_WithSharedAssembly_ReturnsNull()
    {
        var context = new PluginLoadContext(typeof(InProcessRuntimeSuccessfulPlugin).Assembly.Location);

        try
        {
            var assembly = InvokeLoad(context, new AssemblyName("PluginManager.Core"));
            Assert.Null(assembly);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void Load_WithAssemblyNameWithoutName_ReturnsNull()
    {
        var context = new PluginLoadContext(typeof(InProcessRuntimeSuccessfulPlugin).Assembly.Location);

        try
        {
            var assembly = InvokeLoad(context, new AssemblyName());
            Assert.Null(assembly);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void Load_WithResolvableAssembly_LoadsIntoCustomContext()
    {
        var context = new PluginLoadContext(typeof(InProcessRuntimeSuccessfulPlugin).Assembly.Location);

        try
        {
            var assembly = InvokeLoad(context, new AssemblyName(typeof(InProcessRuntimeSuccessfulPlugin).Assembly.GetName().Name!));
            Assert.NotNull(assembly);
            Assert.Same(context, AssemblyLoadContext.GetLoadContext(assembly!));
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void Load_WithUnknownAssembly_ReturnsNull()
    {
        var context = new PluginLoadContext(typeof(InProcessRuntimeSuccessfulPlugin).Assembly.Location);

        try
        {
            var assembly = InvokeLoad(context, new AssemblyName("Definitely.Missing.Assembly"));
            Assert.Null(assembly);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void LoadUnmanagedDll_WithResolvableLibrary_ReturnsHandle()
    {
        var libraryPath = Path.Combine(Environment.SystemDirectory, "kernel32.dll");
        Assert.True(File.Exists(libraryPath));
        var context = new TestPluginLoadContext(typeof(InProcessRuntimeSuccessfulPlugin).Assembly.Location, libraryPath);

        try
        {
            var handle = InvokeLoadUnmanagedDll(context, "kernel32");
            Assert.NotEqual(IntPtr.Zero, handle);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void LoadUnmanagedDll_WithUnknownLibrary_ReturnsZero()
    {
        var context = new PluginLoadContext(typeof(InProcessRuntimeSuccessfulPlugin).Assembly.Location);

        try
        {
            var handle = InvokeLoadUnmanagedDll(context, "definitely-missing-native-library");
            Assert.Equal(IntPtr.Zero, handle);
        }
        finally
        {
            context.Unload();
        }
    }

    private static Assembly? InvokeLoad(PluginLoadContext context, AssemblyName assemblyName)
    {
        var method = typeof(PluginLoadContext).GetMethod("Load", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Assembly?)method!.Invoke(context, [assemblyName]);
    }

    private static IntPtr InvokeLoadUnmanagedDll(PluginLoadContext context, string unmanagedDllName)
    {
        var method = typeof(PluginLoadContext).GetMethod("LoadUnmanagedDll", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (IntPtr)method!.Invoke(context, [unmanagedDllName])!;
    }

    private sealed class TestPluginLoadContext(string pluginPath, string libraryPath) : PluginLoadContext(pluginPath)
    {
        protected override string? ResolveUnmanagedDllPath(string unmanagedDllName)
            => libraryPath;
    }
}
