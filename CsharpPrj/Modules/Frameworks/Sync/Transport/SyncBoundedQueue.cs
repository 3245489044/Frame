using Modules.Sync.Contracts;

namespace Modules.Sync.Transport;

/// <summary>
/// 有界同步队列：只做入队/出队与容量上限，<b>不解释消息语义</b>（只按 <see cref="SyncMessageKind"/> 原样搬运载荷）。
/// <para>并发模型与锁序：单一实例只有一把锁 <c>_gate</c>；锁内不做外部回调、不嵌套第二把锁。
/// 上行/下行是两个独立实例，禁止同时持有两把队列锁。</para>
/// <para>满队列策略：返回 false 表示“这一条没有进去”，不覆盖旧项、不阻塞、不静默丢弃；
/// 由上层按重发/丢弃口径上报。</para>
/// </summary>
public sealed class SyncBoundedQueue
{
    private readonly object _gate = new();
    private readonly SyncMessageKind[] _kinds;
    private readonly object?[] _payloads;
    private int _head;
    private int _count;

    /// <param name="capacity">容量上限；缺省取契约的 <see cref="SyncProtocol.TransportQueueCapacity"/>。</param>
    public SyncBoundedQueue(int capacity = SyncProtocol.TransportQueueCapacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "队列容量必须为正整数。");
        }

        _kinds = new SyncMessageKind[capacity];
        _payloads = new object?[capacity];
    }

    /// <summary>容量上限。</summary>
    public int Capacity => _kinds.Length;

    /// <summary>当前积压条数（快照值，供队列深度观测使用）。</summary>
    public int QueueDepth
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    /// <summary>入队；满则返回 false（不覆盖、不阻塞）。</summary>
    public bool TryEnqueue(SyncMessageKind kind, object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        lock (_gate)
        {
            if (_count >= _kinds.Length)
            {
                return false;
            }

            var index = (_head + _count) % _kinds.Length;
            _kinds[index] = kind;
            _payloads[index] = payload;
            _count++;
            return true;
        }
    }

    /// <summary>出队；空则返回 false。</summary>
    public bool TryDequeue(out SyncMessageKind kind, out object? payload)
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                kind = default;
                payload = null;
                return false;
            }

            kind = _kinds[_head];
            payload = _payloads[_head];
            _payloads[_head] = null;
            _head = (_head + 1) % _kinds.Length;
            _count--;
            return true;
        }
    }

    /// <summary>清空（仅会话关闭或测试准备使用，不参与正常收发路径）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_kinds, 0, _kinds.Length);
            Array.Clear(_payloads, 0, _payloads.Length);
            _head = 0;
            _count = 0;
        }
    }
}
