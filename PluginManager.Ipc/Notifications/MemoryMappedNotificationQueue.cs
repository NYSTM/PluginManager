using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.Json;

namespace PluginManager.Ipc;

/// <summary>
/// メモリマップドファイルを使用した簡易通知キューを提供します。
/// </summary>
public sealed class MemoryMappedNotificationQueue : IDisposable
{
    private const int DefaultCapacity = 64 * 1024;
    private const int HeaderSize = sizeof(int);

    private readonly string _mapName;
    private readonly string _mutexName;
    private readonly MemoryMappedFile _memoryMappedFile;
    private readonly Mutex _mutex;
    private readonly int _capacity;
    private bool _disposed;

    public MemoryMappedNotificationQueue(string mapName, int capacity = DefaultCapacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, HeaderSize);

        _mapName = mapName;
        _mutexName = $"{mapName}.mutex";
        _capacity = capacity;
        _memoryMappedFile = MemoryMappedFile.CreateOrOpen(_mapName, _capacity);
        _mutex = new Mutex(false, _mutexName);
    }

    public string MapName => _mapName;

    public void Enqueue(PluginProcessNotification notification)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(notification);

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(notification) + "\n");

        _mutex.WaitOne();
        try
        {
            using var stream = _memoryMappedFile.CreateViewStream();
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            stream.Position = 0;
            var currentLength = reader.ReadInt32();
            var nextLength = currentLength + bytes.Length;
            if (HeaderSize + nextLength > _capacity)
                throw new InvalidOperationException("通知キューの容量を超えました。");

            stream.Position = HeaderSize + currentLength;
            writer.Write(bytes);
            writer.Flush();

            stream.Position = 0;
            writer.Write(nextLength);
            writer.Flush();
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    public IReadOnlyList<PluginProcessNotification> Drain()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _mutex.WaitOne();
        try
        {
            using var stream = _memoryMappedFile.CreateViewStream();
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            stream.Position = 0;
            var currentLength = reader.ReadInt32();
            if (currentLength <= 0)
                return [];

            stream.Position = HeaderSize;
            var bytes = reader.ReadBytes(currentLength);
            var text = Encoding.UTF8.GetString(bytes);

            stream.Position = 0;
            writer.Write(0);
            writer.Flush();

            return text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonSerializer.Deserialize<PluginProcessNotification>(line))
                .Where(notification => notification is not null)
                .Cast<PluginProcessNotification>()
                .ToList();
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _memoryMappedFile.Dispose();
        _mutex.Dispose();
    }
}
