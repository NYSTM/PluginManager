using System.Collections.Frozen;
using System.Reflection;
using Microsoft.Extensions.Logging;
using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="PluginDiscoverer"/> のテストです。
/// </summary>
public sealed class PluginDiscovererTests
{
    [Fact]
    public void CreateDescriptor_WithAttribute_UsesAttributeMetadata()
    {
        var descriptor = InvokeCreateDescriptor(typeof(AttributedPlugin), "plugins\\attr.dll");

        Assert.NotNull(descriptor);
        Assert.Equal("discoverer-attributed", descriptor.Id);
        Assert.Equal("DiscovererAttributed", descriptor.Name);
        Assert.Equal(new Version(1, 0, 0, 0), descriptor.Version);
        Assert.Equal(typeof(AttributedPlugin).FullName, descriptor.PluginTypeName);
        Assert.Equal("plugins\\attr.dll", descriptor.AssemblyPath);
        Assert.Equal(PluginIsolationMode.OutOfProcess, descriptor.IsolationMode);
        Assert.Equal(["Validation"], descriptor.SupportedStages.Select(x => x.Id).ToArray());
    }

    [Fact]
    public void CreateDescriptor_WithAttributeWithoutStages_UsesDefaultStagesFromAttribute()
    {
        var descriptor = InvokeCreateDescriptor(typeof(AttributedPluginWithoutStages), "plugins\\default-stages.dll");

        Assert.NotNull(descriptor);
        Assert.Equal(
            [PluginStage.PostProcessing.Id, PluginStage.PreProcessing.Id, PluginStage.Processing.Id],
            descriptor.SupportedStages.Select(x => x.Id).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void CreateDescriptor_WithAttributeAndNullFullName_UsesTypeName()
    {
        var descriptor = InvokeCreateDescriptor(new NullFullNameType(typeof(AttributedPlugin)), "plugins\\attr-nullname.dll");

        Assert.NotNull(descriptor);
        Assert.Equal(nameof(AttributedPlugin), descriptor.PluginTypeName);
    }

    [Fact]
    public void CreateDescriptor_WithoutAttribute_UsesFallbackMetadata()
    {
        var descriptor = InvokeCreateDescriptor(typeof(PlainPlugin), "plugins\\plain.dll");

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(PlainPlugin).FullName, descriptor.Id);
        Assert.Equal(nameof(PlainPlugin), descriptor.Name);
        Assert.Equal(typeof(PlainPlugin).Assembly.GetName().Version ?? new Version(1, 0, 0, 0), descriptor.Version);
        Assert.Equal(typeof(PlainPlugin).FullName, descriptor.PluginTypeName);
        Assert.Equal("plugins\\plain.dll", descriptor.AssemblyPath);
        Assert.Equal(
            [PluginStage.PostProcessing.Id, PluginStage.PreProcessing.Id, PluginStage.Processing.Id],
            descriptor.SupportedStages.Select(x => x.Id).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void CreateDescriptor_WithoutAttributeAndNullFullName_UsesTypeName()
    {
        var descriptor = InvokeCreateDescriptor(new NullFullNameType(typeof(PlainPlugin)), "plugins\\plain-nullname.dll");

        Assert.NotNull(descriptor);
        Assert.Equal(nameof(PlainPlugin), descriptor.Id);
        Assert.Equal(nameof(PlainPlugin), descriptor.PluginTypeName);
    }

    [Fact]
    public void CreateDescriptor_WithoutAttributeAndNullAssemblyVersion_UsesDefaultVersion()
    {
        var descriptor = InvokeCreateDescriptor(new NullVersionAssemblyType(typeof(PlainPlugin)), "plugins\\plain-nullversion.dll");

        Assert.NotNull(descriptor);
        Assert.Equal(new Version(1, 0, 0, 0), descriptor.Version);
    }

    [Fact]
    public void Discover_WithCurrentAssemblyFile_ReturnsPlugins()
    {
        var discoverer = new PluginDiscoverer();
        var assemblyDirectory = Path.GetDirectoryName(typeof(PluginDiscovererTests).Assembly.Location)!;
        var assemblyFile = Path.GetFileName(typeof(PluginDiscovererTests).Assembly.Location);

        var descriptors = discoverer.Discover(assemblyDirectory, assemblyFile);

        Assert.Contains(descriptors, x => x.PluginTypeName == typeof(AttributedPlugin).FullName);
        Assert.DoesNotContain(descriptors, x => x.PluginTypeName == typeof(AbstractPluginBase).FullName);
        Assert.DoesNotContain(descriptors, x => x.PluginTypeName == typeof(NonPluginType).FullName);
    }

    [Fact]
    public void DiscoverFromConfiguration_EmptyPluginsPath_ReturnsEmpty()
    {
        var discoverer = new PluginDiscoverer();
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
        {
          "PluginsPath": "",
          "StageOrders": []
        }
        """);

        try
        {
            var descriptors = discoverer.DiscoverFromConfiguration(tempFile);
            Assert.Empty(descriptors);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DiscoverFromAssembly_WithCurrentAssembly_FindsPluginTypes()
    {
        var discoverer = new PluginDiscoverer();
        var descriptors = InvokeDiscoverFromAssembly(discoverer, typeof(PluginDiscovererTests).Assembly.Location);

        Assert.Contains(descriptors, x => x.PluginTypeName == typeof(AttributedPlugin).FullName);
        Assert.Contains(descriptors, x => x.PluginTypeName == typeof(PlainPlugin).FullName);
    }

    [Fact]
    public void DiscoverFromAssembly_WithInvalidAssembly_WithoutLogger_ReturnsEmpty()
    {
        var discoverer = new PluginDiscoverer();
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "not-an-assembly");

        try
        {
            var descriptors = InvokeDiscoverFromAssembly(discoverer, tempFile);
            Assert.Empty(descriptors);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DiscoverFromAssembly_WithInvalidAssembly_LogsDebugAndReturnsEmpty()
    {
        var logger = new TestLogger();
        var discoverer = new PluginDiscoverer(logger);
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "not-an-assembly");

        try
        {
            var descriptors = InvokeDiscoverFromAssembly(discoverer, tempFile);
            Assert.Empty(descriptors);
            Assert.NotEmpty(logger.Messages);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ValidateNoDuplicates_WithDuplicateIds_ThrowsInvalidOperationException()
    {
        var descriptors = new List<PluginDescriptor>
        {
            CreateDescriptor("duplicate-plugin", "a.dll"),
            CreateDescriptor("DUPLICATE-PLUGIN", "b.dll"),
        };

        var ex = Assert.Throws<TargetInvocationException>(() => InvokeValidateNoDuplicates(descriptors));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("duplicate-plugin", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("a.dll", ex.InnerException.Message);
        Assert.Contains("b.dll", ex.InnerException.Message);
    }

    [Fact]
    public void ValidateNoDuplicates_WithUniqueIds_DoesNotThrow()
    {
        var descriptors = new List<PluginDescriptor>
        {
            CreateDescriptor("plugin-a", "a.dll"),
            CreateDescriptor("plugin-b", "b.dll"),
        };

        var ex = Record.Exception(() => InvokeValidateNoDuplicates(descriptors));

        Assert.Null(ex);
    }

    private static PluginDescriptor InvokeCreateDescriptor(Type pluginType, string assemblyPath)
    {
        var method = typeof(PluginDiscoverer).GetMethod("CreateDescriptor", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (PluginDescriptor)method!.Invoke(null, [pluginType, assemblyPath])!;
    }

    private static IReadOnlyList<PluginDescriptor> InvokeDiscoverFromAssembly(PluginDiscoverer discoverer, string assemblyPath)
    {
        var method = typeof(PluginDiscoverer).GetMethod("DiscoverFromAssembly", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return ((IEnumerable<PluginDescriptor>)method!.Invoke(discoverer, [assemblyPath])!).ToList();
    }

    private static void InvokeValidateNoDuplicates(List<PluginDescriptor> descriptors)
    {
        var method = typeof(PluginDiscoverer).GetMethod("ValidateNoDuplicates", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(null, [descriptors]);
    }

    private static PluginDescriptor CreateDescriptor(string id, string assemblyFileName)
        => new(
            id,
            id,
            new Version(1, 0, 0),
            typeof(object).FullName!,
            assemblyFileName,
            new[] { PluginStage.Processing }.ToFrozenSet());

    [Plugin("discoverer-attributed", "DiscovererAttributed", "invalid-version", "Validation", IsolationMode = PluginIsolationMode.OutOfProcess)]
    private sealed class AttributedPlugin : IPlugin
    {
        public string Id => "discoverer-attributed";
        public string Name => "DiscovererAttributed";
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } = new[] { new PluginStage("Validation") }.ToFrozenSet();
        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default) => Task.FromResult<object?>(null);
    }

    [Plugin("discoverer-default-stages", "DiscovererDefaultStages", "1.2.3")]
    private sealed class AttributedPluginWithoutStages : IPlugin
    {
        public string Id => "discoverer-default-stages";
        public string Name => "DiscovererDefaultStages";
        public Version Version => new(1, 2, 3);
        public IReadOnlySet<PluginStage> SupportedStages { get; } = new[] { PluginStage.Processing }.ToFrozenSet();
        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default) => Task.FromResult<object?>(null);
    }

    private abstract class AbstractPluginBase : IPlugin
    {
        public string Id => "abstract";
        public string Name => "abstract";
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } = new[] { PluginStage.Processing }.ToFrozenSet();
        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public abstract Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default);
    }

    private sealed class NonPluginType;

    private sealed class PlainPlugin : IPlugin
    {
        public string Id => nameof(PlainPlugin);
        public string Name => nameof(PlainPlugin);
        public Version Version => new(1, 0, 0);
        public IReadOnlySet<PluginStage> SupportedStages { get; } = new[] { PluginStage.Processing }.ToFrozenSet();
        public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<object?> ExecuteAsync(PluginStage stage, PluginContext context, CancellationToken cancellationToken = default) => Task.FromResult<object?>(null);
    }

    private sealed class NullFullNameType(Type innerType) : TypeDelegator(innerType)
    {
        public override string? FullName => null;
    }

    private sealed class NullVersionAssemblyType(Type innerType) : TypeDelegator(innerType)
    {
        public override Assembly Assembly { get; } = new NullVersionAssembly(innerType.Assembly);
    }

    private sealed class NullVersionAssembly(Assembly innerAssembly) : Assembly
    {
        public override AssemblyName GetName(bool copiedName)
            => new(innerAssembly.GetName(copiedName).Name!);
    }

    private sealed class TestLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}
