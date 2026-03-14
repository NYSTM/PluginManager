using PluginHost;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="Program"/> の引数検証テストです。
/// </summary>
public sealed class ProgramTests
{
    [Fact]
    public async Task Main_WhenNoArguments_Returns1()
    {
        var exitCode = await Program.Main([]);

        Assert.Equal(1, exitCode);
    }
}
