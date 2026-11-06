using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Strategy.Observability.Entity;

namespace Strategy.Observability.Persistence;

/// <summary>内存事件区（E5 测试/热查）：进程内 append-only 列表，单调 seq 续读。</summary>
public sealed class InMemoryEventStore : IEventStore
{
    private readonly List<PersistedEvent> _events = new();
    private readonly object _gate = new();

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task AppendBatchAsync(IReadOnlyList<PersistedEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0) return Task.CompletedTask;
        lock (_gate) _events.AddRange(events);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<PersistedEvent> TimelineAsync(string scopeId, long? afterSeq = null, int limit = 100, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var e in EventWindow.Timeline(SnapshotAll(), scopeId, afterSeq, limit))
        {
            ct.ThrowIfCancellationRequested();
            yield return e;
        }
    }

    public async IAsyncEnumerable<PersistedEvent> EntityChainAsync(EntityId entity, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var e in EventWindow.EntityChain(SnapshotAll(), entity))
        {
            ct.ThrowIfCancellationRequested();
            yield return e;
        }
    }

    private IReadOnlyList<PersistedEvent> SnapshotAll()
    {
        lock (_gate) return _events.ToArray();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
