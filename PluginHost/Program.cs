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
        {
            Console.Error.WriteLine("使用法: PluginHost.exe <pipe-name> [notification-queue-name]");
            return 1;
        }

        var pipeName = args[0];
        var notificationQueueName = args.Length > 1 ? args[1] : null;
        Console.WriteLine($"[PluginHost] プロセス起動: PID={Environment.ProcessId}, Pipe={pipeName}");

        using var shutdownCts = new CancellationTokenSource();
        using var notificationQueue = string.IsNullOrWhiteSpace(notificationQueueName)
            ? null
            : new MemoryMappedNotificationQueue(notificationQueueName);

        notificationQueue?.Enqueue(new PluginProcessNotification
        {
            NotificationType = PluginProcessNotificationType.HostStarted,
            Message = $"PluginHost プロセスが起動しました。PID={Environment.ProcessId}",
            ProcessId = Environment.ProcessId,
        });

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("[PluginHost] シャットダウン要求を受信しました...");
            shutdownCts.Cancel();
        };

        using var registry = new PluginRegistry();
        var handler = new PluginRequestHandler(registry, notificationQueue);
        var server = new PipeServer(pipeName, handler);

        try
        {
            await server.RunAsync(shutdownCts.Token);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PluginHost] 致命的エラー: {ex}");
            return 1;
        }
    }
}
