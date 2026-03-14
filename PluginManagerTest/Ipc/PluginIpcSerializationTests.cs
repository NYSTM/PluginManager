using System.Text.Json;
using PluginManager.Ipc;
using Xunit;

namespace PluginManagerTest;

/// <summary>
/// IPC DTO の JSON シリアライズ互換性テストです。
/// </summary>
public sealed class PluginIpcSerializationTests
{
    [Fact]
    public void PluginHostRequest_SerializeAndDeserialize_RoundTripsValues()
    {
        var request = new PluginHostRequest
        {
            RequestId = "req-1",
            Command = PluginHostCommand.Execute,
            PluginId = "plugin-a",
            AssemblyPath = @"C:\plugins\Sample.dll",
            PluginTypeName = "SamplePlugin.SamplePlugin",
            StageId = "Processing",
            ContextData = new Dictionary<string, JsonElement>
            {
                ["count"] = JsonSerializer.SerializeToElement(5),
                ["name"] = JsonSerializer.SerializeToElement("alpha"),
            },
        };

        var json = JsonSerializer.Serialize(request);
        var restored = JsonSerializer.Deserialize<PluginHostRequest>(json);

        Assert.NotNull(restored);
        Assert.Equal("req-1", restored.RequestId);
        Assert.Equal(PluginHostCommand.Execute, restored.Command);
        Assert.Equal("plugin-a", restored.PluginId);
        Assert.Equal(@"C:\plugins\Sample.dll", restored.AssemblyPath);
        Assert.Equal("SamplePlugin.SamplePlugin", restored.PluginTypeName);
        Assert.Equal("Processing", restored.StageId);
        Assert.NotNull(restored.ContextData);
        Assert.Equal(5, restored.ContextData["count"].GetInt32());
        Assert.Equal("alpha", restored.ContextData["name"].GetString());
    }

    [Fact]
    public void PluginHostResponse_SerializeAndDeserialize_RoundTripsValues()
    {
        var response = new PluginHostResponse
        {
            RequestId = "req-2",
            Success = false,
            ErrorType = nameof(InvalidOperationException),
            ErrorMessage = "実行エラー",
            ResultData = JsonSerializer.SerializeToElement(new { ok = false }),
            ContextData = new Dictionary<string, JsonElement>
            {
                ["status"] = JsonSerializer.SerializeToElement("failed"),
            },
        };

        var json = JsonSerializer.Serialize(response);
        var restored = JsonSerializer.Deserialize<PluginHostResponse>(json);

        Assert.NotNull(restored);
        Assert.Equal("req-2", restored.RequestId);
        Assert.False(restored.Success);
        Assert.Equal(nameof(InvalidOperationException), restored.ErrorType);
        Assert.Equal("実行エラー", restored.ErrorMessage);
        Assert.True(restored.ResultData.HasValue);
        Assert.False(restored.ResultData.Value.GetProperty("ok").GetBoolean());
        Assert.NotNull(restored.ContextData);
        Assert.Equal("failed", restored.ContextData["status"].GetString());
    }

    [Fact]
    public void PluginProcessNotification_SerializeAndDeserialize_RoundTripsValues()
    {
        var createdAt = DateTimeOffset.Now;
        var notification = new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.ExecuteFailed,
            Message = "実行失敗",
            RequestId = "req-3",
            PluginId = "plugin-z",
            StageId = "Finalize",
            ProcessId = 321,
            ErrorType = nameof(TimeoutException),
            ErrorMessage = "タイムアウト",
            CreatedAt = createdAt,
        };

        var json = JsonSerializer.Serialize(notification);
        var restored = JsonSerializer.Deserialize<PluginProcessNotification>(json);

        Assert.NotNull(restored);
        Assert.Equal(PluginProcessNotificationType.ExecuteFailed, restored.NotificationType);
        Assert.Equal("実行失敗", restored.Message);
        Assert.Equal("req-3", restored.RequestId);
        Assert.Equal("plugin-z", restored.PluginId);
        Assert.Equal("Finalize", restored.StageId);
        Assert.Equal(321, restored.ProcessId);
        Assert.Equal(nameof(TimeoutException), restored.ErrorType);
        Assert.Equal("タイムアウト", restored.ErrorMessage);
        Assert.Equal(createdAt, restored.CreatedAt);
    }
}
