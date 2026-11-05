namespace Modules.Threading;

/// <summary>
/// 跨线程安全的有界最近-N 环形缓冲。健康路径只追加不落盘、零开销；仅在取证瞬间读取快照。
/// 容量满后丢弃最旧条目（对齐设计稿“只增、丢最旧；跨线程写 ConcurrentQueue”语义，用锁实现保序）。
/// </summary>
/// <typeparam name="T">条目类型。</typeparam>
public sealed class RingBuffer<T>
{
    private readonly int _capacity;
    private readonly Queue<T> _items = new();
    private readonly object _gate = new();

    public RingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "容量必须 > 0");
        _capacity = capacity;
    }

    /// <summary>最大容量。</summary>
    public int Capacity => _capacity;

    /// <summary>当前条目数。</summary>
    public int Count
    {
        get { lock (_gate) return _items.Count; }
    }

    /// <summary>追加一条；超容量时丢弃最旧。</summary>
    public void Add(T item)
    {
        lock (_gate)
        {
            if (_items.Count >= _capacity) _items.Dequeue();
            _items.Enqueue(item);
        }
    }

    /// <summary>批量追加（保序）。</summary>
    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (_gate)
        {
            foreach (var item in items)
            {
                if (_items.Count >= _capacity) _items.Dequeue();
                _items.Enqueue(item);
            }
        }
    }

    /// <summary>清空（如重置污染 / 新 run 上下文）。</summary>
    public void Clear()
    {
        lock (_gate) _items.Clear();
    }

    /// <summary>读取快照（最旧→最新，保序）；取证瞬间一次性读出。</summary>
    public T[] Snapshot()
    {
        lock (_gate) return _items.ToArray();
    }
}
