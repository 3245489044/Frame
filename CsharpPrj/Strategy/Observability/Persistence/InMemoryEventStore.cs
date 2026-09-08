using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Strategy.Observability.Entity;

namespace Strategy.Observability.Persistence;

/// <summary>内存事件区（E5 测试/热查）：进程内 append-only 列表，单调 seq 续读。</summary>
public sealed class InMemoryEventStore : IEventStore
{
    private readonly List<PersistedEvent> _events = new();
    private readonly object _gate = new();
    private bool _disposed;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task AppendBatchAsync(IReadOnlyList<PersistedEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0) return Task.CompletedTask;
        lock (_gate) _events.AddRange(events);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<PersistedEvent> TimelineAsync(string scopeId, long? afterSeq = null, int limit = 100, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var window = Snapshot(scopeId, afterSeq);
        var count = 0;
        foreach (var e in window)
        {
            ct.ThrowIfCancellationRequested();
            if (count++ >= limit) yield break;
            yield return e;
        }
        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<PersistedEvent> EntityChainAsync(EntityId entity, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var window = Snapshot(entity.ScopeId, null);
        foreach (var e in window)
        {
            ct.ThrowIfCancellationRequested();
            if (string.Equals(e.Event.Entity.EntityType, entity.EntityType, StringComparison.Ordinal))
                yield return e;
        }
        await Task.CompletedTask;
    }

    private IReadOnlyList<PersistedEvent> Snapshot(string scopeId, long? afterSeq)
    {
        lock (_gate)
        {
            var q = _events
                .Where(e => string.Equals(e.ScopeId, scopeId, StringComparison.Ordinal))
                .Where(e => !afterSeq.HasValue || e.EventSeq > afterSeq.Value)
                .OrderBy(e => e.EventSeq)
                .ToList();
            return q;
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
