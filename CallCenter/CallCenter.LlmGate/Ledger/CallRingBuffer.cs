using CallCenter.LlmGate.Model;

namespace CallCenter.LlmGate.Ledger;

/// <summary>进程内环形缓冲（默认 2000 条）。丢弃计数必须可见。</summary>
public sealed class CallRingBuffer
{
    private readonly object _sync = new();
    private readonly LlmCallSpan?[] _items;
    private int _next;
    private int _count;
    private long _dropped;

    public CallRingBuffer(int capacity = 2000)
    {
        _items = new LlmCallSpan?[Math.Max(1, capacity)];
    }

    public int Capacity => _items.Length;

    public long Dropped
    {
        get
        {
            lock (_sync)
            {
                return _dropped;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _count;
            }
        }
    }

    public void Add(LlmCallSpan span)
    {
        ArgumentNullException.ThrowIfNull(span);
        lock (_sync)
        {
            if (_count == _items.Length)
            {
                _dropped++;
            }
            else
            {
                _count++;
            }

            _items[_next] = span;
            _next = (_next + 1) % _items.Length;
        }
    }

    /// <summary>旧 → 新顺序快照（读侧自行反转）。</summary>
    public IReadOnlyList<LlmCallSpan> Snapshot()
    {
        lock (_sync)
        {
            var list = new List<LlmCallSpan>(_count);
            var start = _count == _items.Length ? _next : 0;
            for (var i = 0; i < _count; i++)
            {
                var item = _items[(start + i) % _items.Length];
                if (item is not null)
                {
                    list.Add(item);
                }
            }

            return list;
        }
    }

    public LlmCallSpan? Find(string callId)
    {
        if (string.IsNullOrWhiteSpace(callId))
        {
            return null;
        }

        lock (_sync)
        {
            foreach (var item in _items)
            {
                if (item is not null && string.Equals(item.CallId, callId, StringComparison.Ordinal))
                {
                    return item;
                }
            }
        }

        return null;
    }

    public void Clear()
    {
        lock (_sync)
        {
            Array.Clear(_items);
            _next = 0;
            _count = 0;
            _dropped = 0;
        }
    }
}
