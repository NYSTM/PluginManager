using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PluginManager;
using PluginManager.Ipc;

namespace PluginHost;

/// <summary>
/// 別プロセスでプラグインを実行するホストプログラムです。
/// Named Pipe 経由でメインプロセスからの要求を受け取り、プラグインを操作します。
/// </summary>
internal sealed class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
            return 1;

        var pipeName = args[0];
        var notificationQueueName = args.Length > 1 ? args[1] : null;

        using var shutdownCts = new CancellationTokenSource();
        using var notificationQueue = string.IsNullOrWhiteSpace(notificationQueueName)
            ? null
            : new MemoryMappedNotificationQueue(notificationQueueName);
        var notifier = new PluginHostNotifier(notificationQueue);

        notifier.Notify(
            PluginProcessNotificationType.HostStarted,
            $"PluginHost プロセスが起動しました。PID={Environment.ProcessId}, Pipe={pipeName}");

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            notifier.Notify(
                PluginProcessNotificationType.HostShutdownRequested,
                "PluginHost がシャットダウン要求を受信しました。キャンセルを開始します。");
            shutdownCts.Cancel();
        };

        using var registry = new PluginRegistry(notifier);
        var handler = new PluginRequestHandler(registry, notifier);
        var server = new PipeServer(pipeName, handler, notifier);

        try
        {
            await server.RunAsync(shutdownCts.Token);
            return 0;
        }
        catch (Exception ex)
        {
            notifier.Notify(
                PluginProcessNotificationType.HostFatalError,
                "PluginHost で致命的エラーが発生しました。",
                errorType: ex.GetType().Name,
                errorMessage: ex.Message);
            return 1;
        }
    }
}
