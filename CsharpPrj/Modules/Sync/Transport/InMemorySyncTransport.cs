using System;

namespace Modules.Sync.Transport;

/// <summary>
/// 进程内同步传输：一个实例 = <b>一个方向</b>的有界队列。
/// <see cref="TrySend"/> 同步入队、<see cref="TryReceive"/> 同步出队；无真实网络栈、无线程、无延时、无重排。
/// <para>方向分离：上行（client → server）与下行（server → client）由 <see cref="CreatePair"/> 一次建出一对，
/// 两个队列彼此独立；锁序见 <see cref="SyncBoundedQueue"/>（单锁、无嵌套）。</para>
/// <para>[契约依赖 · 等待 SY-00] <c>ISyncTransport</c>（<c>Send</c> / <c>TryReceive</c>，有界队列）与
/// <c>TransportQueueCapacity</c> 尚未落地：本类当前只提供同语义的 <see cref="TrySend"/> / <see cref="TryReceive"/>，
/// 接口落地后在本类内改为实现该端口并转发，<b>不改动</b>内部队列实现，也不在传输层定义任何消息类型。</para>
/// </summary>
public sealed class InMemorySyncTransport
{
    private readonly SyncBoundedQueue _queue;

    /// <param name="capacity">容量上限；契约落地后由 <c>TransportQueueCapacity</c> 传入。</param>
    public InMemorySyncTransport(int capacity)
    {
        _queue = new SyncBoundedQueue(capacity);
    }

    /// <summary>容量上限。</summary>
    public int Capacity => _queue.Capacity;

    /// <summary>当前积压条数（供队列深度观测）。</summary>
    public int QueueDepth => _queue.Depth;

    /// <summary>同步入队；队列满返回 false，由调用方决定上报口径。</summary>
    public bool TrySend(object payload) => _queue.TryEnqueue(payload);

    /// <summary>同步出队；队列空返回 false。</summary>
    public bool TryReceive(out object? payload) => _queue.TryDequeue(out payload);

    /// <summary>清空本方向队列（会话关闭时使用）。</summary>
    public void Clear() => _queue.Clear();

    /// <summary>
    /// 建出一对方向相反的传输：<c>Uplink</c> = client → server，<c>Downlink</c> = server → client。
    /// </summary>
    public static (InMemorySyncTransport Uplink, InMemorySyncTransport Downlink) CreatePair(int capacity)
        => (new InMemorySyncTransport(capacity), new InMemorySyncTransport(capacity));
}
