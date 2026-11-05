using System;

namespace Modules.Sync.Transport;

/// <summary>
/// 有界同步队列：只做入队/出队与容量上限，不解释载荷语义（不区分消息类型、不解析字段）。
/// <para>并发模型与锁序：单一实例只有一把锁 <c>_gate</c>；锁内不做任何外部回调、不调用其它实例、
/// 不嵌套第二把锁。上行与下行是<b>两个独立实例</b>，各自进出互不相干；
/// 若某调用方需要同时操作两者，必须先释放上行再取下行，<b>禁止同时持有两把队列锁</b>。</para>
/// <para>满队列策略：<see cref="TryEnqueue"/> 返回 false 表示“这一条没有进去”，不覆盖旧项、不阻塞、不丢弃静默；
/// 调用方据此上报（观测计数由上层端口负责，本层不引用指标常量）。</para>
/// </summary>
public sealed class SyncBoundedQueue
{
    private readonly object _gate = new();
    private readonly object?[] _items;
    private int _head;
    private int _count;

    /// <param name="capacity">容量上限；契约落地后由 <c>TransportQueueCapacity</c> 传入，本层不写死常量。</param>
    public SyncBoundedQueue(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "队列容量必须为正整数。");
        }

        _items = new object?[capacity];
    }

    /// <summary>容量上限。</summary>
    public int Capacity => _items.Length;

    /// <summary>当前积压条数（快照值，供队列深度观测使用）。</summary>
    public int Depth
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    /// <summary>入队；满则返回 false（不覆盖、不阻塞、不静默丢弃）。</summary>
    public bool TryEnqueue(object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        lock (_gate)
        {
            if (_count >= _items.Length)
            {
                return false;
            }

            _items[(_head + _count) % _items.Length] = payload;
            _count++;
            return true;
        }
    }

    /// <summary>出队；空则返回 false。</summary>
    public bool TryDequeue(out object? payload)
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                payload = null;
                return false;
            }

            payload = _items[_head];
            _items[_head] = null;
            _head = (_head + 1) % _items.Length;
            _count--;
            return true;
        }
    }

    /// <summary>清空（仅用于会话关闭或测试准备，不参与正常收发路径）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_items, 0, _items.Length);
            _head = 0;
            _count = 0;
        }
    }
}
