using System.Text;
using System.Text.Json;
using PluginManager;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// <see cref="PluginStageJsonConverter"/> のテストです。
/// </summary>
public sealed class PluginStageJsonConverterTests
{
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new PluginStageJsonConverter());
        return options;
    }

    [Fact]
    public void Deserialize_Null_ReturnsNull()
    {
        var value = JsonSerializer.Deserialize<PluginStage?>("null", CreateOptions());
        Assert.Null(value);
    }

    [Fact]
    public void Deserialize_WhitespaceString_ReturnsNull()
    {
        var value = JsonSerializer.Deserialize<PluginStage?>("\"   \"", CreateOptions());
        Assert.Null(value);
    }

    [Fact]
    public void Deserialize_String_ReturnsPluginStage()
    {
        var value = JsonSerializer.Deserialize<PluginStage?>("\"Validation\"", CreateOptions());
        Assert.NotNull(value);
        Assert.Equal("Validation", value!.Id);
    }

    [Fact]
    public void Read_WithNullToken_ReturnsNull()
    {
        var converter = new PluginStageJsonConverter();
        var reader = new Utf8JsonReader("null"u8.ToArray());
        Assert.True(reader.Read());

        var value = converter.Read(ref reader, typeof(PluginStage), new JsonSerializerOptions());

        Assert.Null(value);
    }

    [Fact]
    public void Read_WithStringToken_ReturnsPluginStage()
    {
        var converter = new PluginStageJsonConverter();
        var reader = new Utf8JsonReader("\"Validation\""u8.ToArray());
        Assert.True(reader.Read());

        var value = converter.Read(ref reader, typeof(PluginStage), new JsonSerializerOptions());

        Assert.NotNull(value);
        Assert.Equal("Validation", value!.Id);
    }

    [Fact]
    public void Write_WithNull_WritesNullValue()
    {
        var converter = new PluginStageJsonConverter();
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        converter.Write(writer, null, new JsonSerializerOptions());
        writer.Flush();

        Assert.Equal("null", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void Serialize_Null_WritesNull()
    {
        var json = JsonSerializer.Serialize<PluginStage?>(null, CreateOptions());
        Assert.Equal("null", json);
    }

    [Fact]
    public void Serialize_Stage_WritesStageId()
    {
        var json = JsonSerializer.Serialize<PluginStage?>(new PluginStage("Validation"), CreateOptions());
        Assert.Equal("\"Validation\"", json);
    }
}
