using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Strategy.Observability.Entity;

namespace Strategy.Observability.Persistence;

/// <summary>
/// append-only JSONL 文件事件区（E5）：每行一条 <see cref="PersistedEvent"/>，重启不丢、零外部依赖，
/// 满足 Batch1 持久化验收；供生产把同一端口适配到 SQLite / IZoneStore 事件区。
/// 单写者（PersistentEventSink.FlushAsync 显式驱动）持文件锁追加；查询按需全量读后过滤（原型语义）。
/// </summary>
public sealed class FileAppendEventStore : IEventStore, IEventStoreDiagnostics
{
    private static readonly JsonSerializerOptions Opt = new() { PropertyNameCaseInsensitive = true };

    private readonly string _path;
    private readonly object _writeGate = new();
    private long _corruptLines;

    public FileAppendEventStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
        _path = path;
    }

    public string Path => _path;

    /// <summary>读侧累计跳过的损坏行数（坏行不静默：至少计数可见）。</summary>
    public long CorruptLineCount => Interlocked.Read(ref _corruptLines);

    public Task InitializeAsync(CancellationToken ct = default)
    {
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        return Task.CompletedTask;
    }

    public Task AppendBatchAsync(IReadOnlyList<PersistedEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0) return Task.CompletedTask;
        lock (_writeGate)
        {
            var sb = new StringBuilder();
            foreach (var e in events) sb.Append(JsonSerializer.Serialize(e, Opt)).Append('\n');
            File.AppendAllText(_path, sb.ToString(), Encoding.UTF8);
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<PersistedEvent> TimelineAsync(string scopeId, long? afterSeq = null, int limit = 100, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var e in EventWindow.Timeline(ReadAll(), scopeId, afterSeq, limit))
        {
            ct.ThrowIfCancellationRequested();
            yield return e;
        }
    }

    public async IAsyncEnumerable<PersistedEvent> EntityChainAsync(EntityId entity, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var e in EventWindow.EntityChain(ReadAll(), entity))
        {
            ct.ThrowIfCancellationRequested();
            yield return e;
        }
    }

    private IReadOnlyList<PersistedEvent> ReadAll()
    {
        lock (_writeGate)
        {
            if (!File.Exists(_path)) return Array.Empty<PersistedEvent>();
            var list = new List<PersistedEvent>();
            foreach (var line in File.ReadLines(_path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var evt = JsonSerializer.Deserialize<PersistedEvent>(line, Opt);
                    if (evt is not null) list.Add(evt);
                }
                catch (JsonException)
                {
                    // 容忍损坏行继续读（只读侧不影响已写批），但必须计数可见（不静默吞掉）。
                    Interlocked.Increment(ref _corruptLines);
                }
            }
            return list;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
