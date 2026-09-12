using DataCenter.Protocol.Roots;
using Operation.StateBlock.Roots;

namespace DataCenter.Hosting;

/// <summary>
/// 运行时槽分区宿主（Hosting 实现）—— 根分区（<see cref="IRuntimeSlot"/>）的唯一承载者。
/// 语义：装配期注册（同键同实例幂等 / 同键异实例 fail-fast）；运行期只读；处置期按注册逆序释放
/// 分区内 <see cref="IDisposable"/> 对象（先停写再释放，避免"边释放边被调用"）。
/// 权威: 设计文档 §3.1 通道 B、§6 D2。
/// </summary>
public sealed class HostRuntimeSlot : IRuntimeSlot, IDisposable
{
    private readonly ConcurrentDictionary<string, OrderedRuntimeSlotPartition> _partitions = new(StringComparer.Ordinal);
    private int _disposed;

    /// <summary>宿主是否已释放槽（释放后一切访问 fail-fast）。</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public IReadOnlyCollection<string> Domains
    {
        get
        {
            ThrowIfDisposed();
            return _partitions.Keys.OrderBy(static k => k, StringComparer.Ordinal).ToArray();
        }
    }

    public IRuntimeSlotPartition Partition(string domainKey)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(domainKey))
            throw new ArgumentException("域键不能为空（槽按域键细分）", nameof(domainKey));
        return _partitions.GetOrAdd(domainKey, static key => new OrderedRuntimeSlotPartition(key));
    }

    /// <summary>尝试取已存在的分区（诊断/装配核对用；不隐式创建）。</summary>
    public bool TryGetPartition(string domainKey, out IRuntimeSlotPartition? partition)
    {
        ThrowIfDisposed();
        var found = _partitions.TryGetValue(domainKey, out var value);
        partition = found ? value : null;
        return found;
    }

    /// <summary>释放全部分区（按注册逆序释放分区内对象；失败聚合后抛出，不静默吞掉）。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var failures = new List<Exception>();
        foreach (var partition in _partitions.Values)
            partition.Dispose(failures);
        _partitions.Clear();

        if (failures.Count > 0)
            throw new AggregateException("运行时槽释放存在失败项（已尽力释放其余槽）。", failures);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(HostRuntimeSlot));
    }

}
