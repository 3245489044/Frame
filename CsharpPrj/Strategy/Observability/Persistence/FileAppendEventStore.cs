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
public sealed class FileAppendEventStore : IEventStore
{
    private static readonly JsonSerializerOptions Opt = new() { PropertyNameCaseInsensitive = true };

    private readonly string _path;
    private readonly object _writeGate = new();

    public FileAppendEventStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
        _path = path;
    }

    public string Path => _path;

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
        var all = ReadAll();
        var count = 0;
        foreach (var e in all)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.Equals(e.ScopeId, scopeId, StringComparison.Ordinal)) continue;
            if (afterSeq.HasValue && e.EventSeq <= afterSeq.Value) continue;
            if (count++ >= limit) yield break;
            yield return e;
        }
        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<PersistedEvent> EntityChainAsync(EntityId entity, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var e in ReadAll())
        {
            ct.ThrowIfCancellationRequested();
            if (!string.Equals(e.ScopeId, entity.ScopeId, StringComparison.Ordinal)) continue;
            if (!string.Equals(e.Event.Entity.EntityType, entity.EntityType, StringComparison.Ordinal)) continue;
            yield return e;
        }
        await Task.CompletedTask;
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
                    // 容忍损坏行（只读侧不影响已写批）
                }
            }
            return list.OrderBy(e => e.EventSeq).ToList();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
