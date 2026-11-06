using System.Runtime.CompilerServices;
using Strategy.Observability.Entity;

namespace Strategy.Observability.Persistence;

/// <summary>
/// 有界内存事件区（E5 实时形态）：append-only 语义 + 容量上限（超限丢最旧并计入 <see cref="DroppedCount"/> 可见账）。
/// <b>零落盘</b>：语义观测的离线读面改由实时出口（<c>Publish.LiveEventPipe</c>）承担；
/// 容量 = 回溯窗口上界，进程内查询与实时出口读<b>同一份</b>事实（单源，禁第二记分牌）。
/// </summary>
public sealed class RingEventStore : IEventStore, IRecentEvents, IEventStoreDiagnostics
{
    /// <summary>缺省容量（条）：约覆盖一次会话的链式回溯窗口。</summary>
    public const int DefaultCapacity = 8192;

    private readonly Queue<PersistedEvent> _events = new();
    private readonly object _gate = new();
    private readonly int _capacity;
    private long _appended;
    private long _dropped;

    public RingEventStore(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "容量须为正数");
        _capacity = capacity;
    }

    /// <summary>容量（条）。</summary>
    public int Capacity => _capacity;

    /// <summary>当前在环条数。</summary>
    public int Count
    {
        get { lock (_gate) return _events.Count; }
    }

    /// <summary>累计接收条数（只增）。</summary>
    public long AppendedCount => Interlocked.Read(ref _appended);

    /// <summary>因容量超限被丢弃的条数（生命周期累计；是否截断“本次窗口”由读面另行判定）。</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <summary>环形区不解析落盘行，故读侧坏行恒为 0（接口统一口径）。</summary>
    public long CorruptLineCount => 0;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task AppendBatchAsync(IReadOnlyList<PersistedEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0) return Task.CompletedTask;

        lock (_gate)
        {
            foreach (var e in events)
            {
                _events.Enqueue(e);
                Interlocked.Increment(ref _appended);

                var overflow = _events.Count - _capacity;
                if (overflow <= 0) continue;
                for (var i = 0; i < overflow; i++) _events.Dequeue();
                Interlocked.Add(ref _dropped, overflow);
            }
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<PersistedEvent> TimelineAsync(
        string scopeId, long? afterSeq = null, int limit = 100, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var e in EventWindow.Timeline(SnapshotAll(), scopeId, afterSeq, limit))
        {
            ct.ThrowIfCancellationRequested();
            yield return e;
        }
    }

    public async IAsyncEnumerable<PersistedEvent> EntityChainAsync(
        EntityId entity, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var e in EventWindow.EntityChain(SnapshotAll(), entity))
        {
            ct.ThrowIfCancellationRequested();
            yield return e;
        }
    }

    /// <summary>最近 <paramref name="limit"/> 条（接收序）；供实时出口回放。</summary>
    public IReadOnlyList<PersistedEvent> RecentSnapshot(int limit)
    {
        if (limit <= 0) return Array.Empty<PersistedEvent>();
        lock (_gate)
        {
            var skip = Math.Max(0, _events.Count - limit);
            return _events.Skip(skip).ToList();
        }
    }

    private IReadOnlyList<PersistedEvent> SnapshotAll()
    {
        lock (_gate) return _events.ToArray();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
