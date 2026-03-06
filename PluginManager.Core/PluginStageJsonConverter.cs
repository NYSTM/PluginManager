using System.Text.Json;
using System.Text.Json.Serialization;

namespace PluginManager;

/// <summary>
/// <see cref="PluginStage"/> を JSON 文字列と相互変換するコンバーターです。
/// 任意の文字列を <see cref="PluginStage"/> として受け入れます。
/// </summary>
internal sealed class PluginStageJsonConverter : JsonConverter<PluginStage?>
{
    /// <inheritdoc/>
    public override PluginStage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var value = reader.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : new PluginStage(value);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, PluginStage? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Id);
    }
}
