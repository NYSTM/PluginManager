using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PluginManager;

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
            Console.Error.WriteLine("使用法: PluginHost.exe <pipe-name>");
            return 1;
        }

        var pipeName = args[0];
        Console.WriteLine($"[PluginHost] プロセス起動: PID={Environment.ProcessId}, Pipe={pipeName}");

        using var shutdownCts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("[PluginHost] シャットダウン要求を受信しました...");
            shutdownCts.Cancel();
        };

        using var registry = new PluginRegistry();
        var handler = new PluginRequestHandler(registry);
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
