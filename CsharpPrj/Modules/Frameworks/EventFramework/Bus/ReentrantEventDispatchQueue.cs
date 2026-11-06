using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Modules.EventFramework.Bus;

/// <summary>
/// 可重入事件派发队列（单消费者）：收敛「入队 → 重入守卫 → 逐项 drain → 结果结算 → 守卫复位」样板。
/// <para>
/// 语义：首次进入 drain 的调用为外层消费者，负责把队列排空；drain 期间再次 <see cref="Enqueue"/> 只入队，
/// 由外层循环消费（<see cref="TryEnterDrain"/> 返回 false）。队列本身不解释元素语义，
/// 结算动作由调用方在 <see cref="Drain"/> 的 handler 中完成。
/// </para>
/// <para>
/// 与 <see cref="EventQueue"/> 分工：后者是带容量上限/溢出策略的后台异步传播队列；
/// 本类只做同步单消费者重入守卫 + 排空，不持有总线、不启动线程、不做溢出丢弃。
/// 非阻塞、无锁队列（<see cref="ConcurrentQueue{T}"/>），守卫为 Interlocked 单标志。
/// </para>
/// </summary>
public sealed class ReentrantEventDispatchQueue<T>
{
    private readonly ConcurrentQueue<T> _queue = new();
    private int _dispatching;

    /// <summary>当前积压条目数（诊断/测试用）。</summary>
    public int Count => _queue.Count;

    /// <summary>是否正处于分发中（外层消费者已持有守卫）。</summary>
    public bool IsDispatching => Volatile.Read(ref _dispatching) == 1;

    /// <summary>入队（不触发分发；是否消费由调用方的守卫结果决定）。</summary>
    public void Enqueue(T item) => _queue.Enqueue(item);

    /// <summary>
    /// 尝试成为外层消费者：返回 true 表示本次调用获得分发权（调用方必须在 finally 中 <see cref="ExitDrain"/>）；
    /// 返回 false 表示已有外层消费者在 drain，本次只入队即可。
    /// </summary>
    public bool TryEnterDrain() => Interlocked.Exchange(ref _dispatching, 1) == 0;

    /// <summary>释放分发权（必须与成功的 <see cref="TryEnterDrain"/> 一一配对）。</summary>
    public void ExitDrain() => Interlocked.Exchange(ref _dispatching, 0);

    /// <summary>
    /// 排空当前队列并对每项执行 <paramref name="handler"/>（由外层消费者调用）。
    /// handler 内再次入队的条目会在同一 drain 中被继续消费（重入语义）。
    /// 返回本次处理的条目数。
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> 为 null。</exception>
    public int Drain(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        int processed = 0;
        while (_queue.TryDequeue(out var item))
        {
            handler(item);
            processed++;
        }

        return processed;
    }

    /// <summary>尝试取出单条（供需要在 drain 中自行控制节奏的调用方使用）。</summary>
    public bool TryDequeue(out T item)
    {
        if (_queue.TryDequeue(out var dequeued))
        {
            item = dequeued;
            return true;
        }

        item = default!;
        return false;
    }

    /// <summary>清空（测试/重建用；不结算任何条目）。</summary>
    public void Clear()
    {
        while (_queue.TryDequeue(out _))
        {
        }
    }
}
