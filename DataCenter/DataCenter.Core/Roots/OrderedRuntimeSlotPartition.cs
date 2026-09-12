using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using DataCenter.Protocol.Roots;

namespace Operation.StateBlock.Roots;

/// <summary>
/// 运行时槽分区<b>单一实现</b>（F-01/F-15）—— 槽语义只此一处：
/// 装配期注册（同键同实例幂等 / 同键异实例 fail-fast）、并发首访原子装配（<see cref="GetOrAdd{T}"/>）、
/// 处置期覆盖（<see cref="Replace"/>）、处置期按注册<b>逆序</b>释放分区内 <see cref="IDisposable"/> 对象。
/// <para>
/// 承载方：宿主根分区（<c>DataCenter.Hosting.HostRuntimeSlot</c>）与无根回落分区
/// （宿主未装配根时的进程本地容器）共用本类型，消除逐行同构的两份实现。
/// </para>
/// </summary>
public sealed class OrderedRuntimeSlotPartition : IRuntimeSlotPartition
{
    private readonly ConcurrentDictionary<string, object> _values = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();
    private readonly object _gate = new();
    private int _disposed;

    public OrderedRuntimeSlotPartition(string domainKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainKey);
        DomainKey = domainKey;
    }

    public string DomainKey { get; }

    public IReadOnlyCollection<string> Keys
    {
        get
        {
            ThrowIfDisposed();
            lock (_gate)
                return _order.ToArray();
        }
    }

    public void Register(string key, object value)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
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

    /// <summary>取或装配（运行期并发首访路径，F-20）：装配在锁内完成 ⇒ 工厂至多生效一次。</summary>
    public T GetOrAdd<T>(string key, Func<T> factory) where T : class
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        lock (_gate)
        {
            if (_values.TryGetValue(key, out var existingRaw))
            {
                if (existingRaw is T existing)
                    return existing;
                throw new InvalidOperationException(
                    $"运行时槽分区 '{DomainKey}' 的键 '{key}' 已注册类型不兼容的实例（期望 {typeof(T).Name}，实得 {existingRaw.GetType().Name}）。");
            }

            var created = factory();
            ArgumentNullException.ThrowIfNull(created);
            _values[key] = created;
            _order.Add(key);
            return created;
        }
    }

    public void Replace(string key, object value)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        lock (_gate)
        {
            if (_values.TryGetValue(key, out var existing) && ReferenceEquals(existing, value))
                return; // 同实例幂等

            if (!_values.ContainsKey(key))
                _order.Add(key);
            _values[key] = value;
        }
    }

    public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T? value) where T : class
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
        ArgumentNullException.ThrowIfNull(failures);
        DisposeCore(failures);
    }

    public void Dispose() => DisposeCore(null);

    private void DisposeCore(List<Exception>? failures)
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
                    failures?.Add(new InvalidOperationException(
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