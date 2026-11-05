using Modules.Sync.Contracts;

namespace Modules.Sync.Transport;

/// <summary>
/// 进程内同步传输：实现 <see cref="ISyncTransport"/>，一个实例 = <b>一个方向</b>的有界队列。
/// <see cref="Send"/> 同步入队、<see cref="TryReceive"/> 同步出队；无网络栈、无线程、无延时、无重排。
/// <para>方向分离：上行（client → server）与下行（server → client）由 <see cref="CreatePair"/> 一次建出一对；
/// 锁序见 <see cref="SyncBoundedQueue"/>（单锁、无嵌套）。</para>
/// </summary>
public sealed class InMemorySyncTransport : ISyncTransport
{
    private readonly SyncBoundedQueue _queue;

    /// <param name="capacity">容量上限；缺省取契约的 <see cref="SyncProtocol.TransportQueueCapacity"/>。</param>
    public InMemorySyncTransport(int capacity = SyncProtocol.TransportQueueCapacity)
    {
        _queue = new SyncBoundedQueue(capacity);
    }

    /// <inheritdoc />
    public int Capacity => _queue.Capacity;

    /// <inheritdoc />
    public int QueueDepth => _queue.QueueDepth;

    /// <inheritdoc />
    public bool Send(SyncMessageKind kind, object payload) => _queue.TryEnqueue(kind, payload);

    /// <inheritdoc />
    public bool TryReceive(out SyncMessageKind kind, out object? payload)
        => _queue.TryDequeue(out kind, out payload);

    /// <summary>清空本方向队列（会话关闭时使用）。</summary>
    public void Clear() => _queue.Clear();

    /// <summary>建出一对方向相反的传输：<c>Uplink</c> = client → server，<c>Downlink</c> = server → client。</summary>
    public static (InMemorySyncTransport Uplink, InMemorySyncTransport Downlink) CreatePair(
        int capacity = SyncProtocol.TransportQueueCapacity)
        => (new InMemorySyncTransport(capacity), new InMemorySyncTransport(capacity));
}
