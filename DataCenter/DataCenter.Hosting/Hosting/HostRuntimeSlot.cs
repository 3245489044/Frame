using DataCenter.Protocol.Roots;

namespace DataCenter.Hosting;

/// <summary>
/// 运行时槽分区宿主（Hosting 实现）—— 根分区（<see cref="IRuntimeSlot"/>）的唯一承载者。
/// 语义：装配期注册（同键同实例幂等 / 同键异实例 fail-fast）；运行期只读；处置期按注册逆序释放
/// 分区内 <see cref="IDisposable"/> 对象（先停写再释放，避免"边释放边被调用"）。
/// 权威: 设计文档 §3.1 通道 B、§6 D2。
/// </summary>
public sealed class HostRuntimeSlot : IRuntimeSlot, IDisposable
{
    private readonly ConcurrentDictionary<string, SlotPartition> _partitions = new(StringComparer.Ordinal);
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
        return _partitions.GetOrAdd(domainKey, static key => new SlotPartition(key));
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

    private sealed class SlotPartition(string domainKey) : IRuntimeSlotPartition
    {
        private readonly ConcurrentDictionary<string, object> _values = new(StringComparer.Ordinal);
        private readonly List<string> _order = new();
        private readonly object _gate = new();
        private int _disposed;

        public string DomainKey { get; } = domainKey;

        public IReadOnlyCollection<string> Keys
        {
            get
            {
                ThrowIfDisposed();
                return _order.ToArray();
            }
        }

        public void Register(string key, object value)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("槽键不能为空", nameof(key));
            ArgumentNullException.ThrowIfNull(value);

            lock (_gate)
            {
                if (_values.TryGetValue(key, out var existing))
                {
                    // 同键同实例 = 幂等（装配重入）；同键异实例 = 第二份运行时（DF8 禁止）
                    if (ReferenceEquals(existing, value))
                        return;
                    throw new InvalidOperationException(
                        $"运行时槽分区 '{DomainKey}' 的键 '{key}' 已注册不同实例（DF8：禁止第二份运行时/第二根）。");
                }

                _values[key] = value;
                _order.Add(key);
            }
        }

        public bool TryGet<T>(string key, out T? value) where T : class
        {
            ThrowIfDisposed();
            if (_values.TryGetValue(key, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }

            value = null;
            return false;
        }

        public T Get<T>(string key) where T : class
        {
            ThrowIfDisposed();
            if (_values.TryGetValue(key, out var raw) && raw is T typed)
                return typed;
            throw new KeyNotFoundException($"运行时槽分区 '{DomainKey}' 中不存在类型为 {typeof(T).Name} 的槽 '{key}'。");
        }

        public object Get(string key)
        {
            ThrowIfDisposed();
            return _values.TryGetValue(key, out var raw)
                ? raw
                : throw new KeyNotFoundException($"运行时槽分区 '{DomainKey}' 中不存在槽 '{key}'。");
        }

        /// <summary>释放分区（按注册逆序；可释放对象逐个释放，失败记入 <paramref name="failures"/>）。</summary>
        public void Dispose(List<Exception> failures)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            lock (_gate)
            {
                for (var i = _order.Count - 1; i >= 0; i--)
                {
                    if (!_values.TryGetValue(_order[i], out var value) || value is not IDisposable disposable)
                        continue;
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        failures.Add(new InvalidOperationException(
                            $"运行时槽分区 '{DomainKey}' 的槽 '{_order[i]}' 释放失败。", ex));
                    }
                }

                _values.Clear();
                _order.Clear();
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException($"RuntimeSlotPartition[{DomainKey}]");
        }
    }
}
