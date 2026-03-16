using PluginManager.Ipc;

namespace PluginManager;

/// <summary>
/// `PluginHost` へのシャットダウン要求を短いタイムアウト付きで送信します。
/// </summary>
internal static class PluginHostShutdownHelper
{
    public const int DefaultShutdownTimeoutMilliseconds = 3000;

    public static void SendShutdown(PluginHostClient client, int timeoutMilliseconds = DefaultShutdownTimeoutMilliseconds)
        => SendShutdownAsync(client, CancellationToken.None, timeoutMilliseconds).GetAwaiter().GetResult();

    public static async Task SendShutdownAsync(
        PluginHostClient client,
        CancellationToken cancellationToken = default,
        int timeoutMilliseconds = DefaultShutdownTimeoutMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeoutMilliseconds, 0);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMilliseconds);

        try
        {
            await client.SendRequestAsync(CreateShutdownRequest(), timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"PluginHost の Shutdown 応答が {timeoutMilliseconds}ms 以内に返りませんでした。");
        }
    }

    private static PluginHostRequest CreateShutdownRequest()
        => new()
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Command = PluginHostCommand.Shutdown,
        };
}
