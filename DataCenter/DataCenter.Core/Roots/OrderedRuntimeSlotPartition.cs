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

    public bool Register(string key, object value, SlotWritePolicy policy = SlotWritePolicy.Strict)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        lock (_gate)
        {
            _values.TryGetValue(key, out var existing);
            switch (ClassifyWrite(existing, value, policy))
            {
                case SlotWriteOutcome.Added:
                    _values[key] = value;
                    _order.Add(key);
                    return false;
                case SlotWriteOutcome.Idempotent:
                    return false; // 同实例幂等（装配重入）
                case SlotWriteOutcome.Replaced:
                    _values[key] = value; // 跨代重建覆盖（同名逻辑类型）
                    return true;
                default:
                    throw new InvalidOperationException(
                        $"运行时槽分区 '{DomainKey}' 的键 '{key}' 已注册不同实例（DF8：禁止第二份运行时/第二根）。");
            }
        }
    }

    /// <summary>取或装配（运行期并发首访路径，F-20）：装配在锁内完成 ⇒ 工厂至多生效一次。
    /// 工厂内（同线程）重入注册同键时不得被覆盖（F-44）：以先登为准返回既有实例。</summary>
    public T GetOrAdd<T>(string key, Func<T> factory) where T : class
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        lock (_gate)
        {
            if (_values.TryGetValue(key, out var existingRaw))
                return ResolveRegisteredOrRebuild<T>(key, existingRaw, factory);

            var created = factory();
            ArgumentNullException.ThrowIfNull(created);

            // 工厂内重入已登记同键（装配期自注册）→ 后写不得覆盖先登
            if (_values.TryGetValue(key, out var reentrantRaw))
                return ResolveRegistered<T>(key, reentrantRaw);

            _values[key] = created;
            _order.Add(key);
            return created;
        }
    }

    /// <summary>已登记实例的类型兼容解析（不符 = fail-fast，不给静默换类型）。</summary>
    private T ResolveRegistered<T>(string key, object registeredRaw) where T : class
        => registeredRaw as T
           ?? throw new InvalidOperationException(
               $"运行时槽分区 '{DomainKey}' 的键 '{key}' 已注册类型不兼容的实例（期望 {typeof(T).Name}，实得 {registeredRaw.GetType().Name}）。");

    /// <summary>
    /// 已登记实例解析 + **跨加载上下文换代重建**（GetOrAdd 单一策略：同类型复用 / 同名类型重建 / 其余 fail-fast）。
    /// 同名类型（程序集简单名 + 类型全名一致、Type 身份不同，如换代后新 ALC 重载的同源程序集）视为**旧代陈旧实例**，
    /// 以 <paramref name="factory"/> 重建并覆盖（对齐装配期处置语义：装配期 Register / 换代重建 / 运行期只读）；
    /// 其他类型不符仍 fail-fast（不给静默换类型）。
    /// </summary>
    private T ResolveRegisteredOrRebuild<T>(string key, object registeredRaw, Func<T> factory) where T : class
    {
        if (registeredRaw is T typed)
            return typed;

        if (IsSameLogicalType(registeredRaw.GetType(), typeof(T)))
        {
            var rebuilt = factory();
            ArgumentNullException.ThrowIfNull(rebuilt);
            _values[key] = rebuilt;
            return rebuilt;
        }

        throw new InvalidOperationException(
            $"运行时槽分区 '{DomainKey}' 的键 '{key}' 已注册类型不兼容的实例（期望 {typeof(T).Name}，实得 {registeredRaw.GetType().Name}）。");
    }

    /// <summary>同一逻辑类型判定（跨加载上下文）：程序集简单名 + 类型全名一致（忽略 ALC 身份）。</summary>
    private static bool IsSameLogicalType(Type left, Type right)
        => string.Equals(left.FullName, right.FullName, StringComparison.Ordinal)
           && string.Equals(left.Assembly.GetName().Name, right.Assembly.GetName().Name, StringComparison.Ordinal);

    /// <summary>写三态（<see cref="Register"/> / <see cref="GetOrAdd{T}"/> 共用的单份判定结果）。</summary>
    private enum SlotWriteOutcome
    {
        Added,
        Idempotent,
        Replaced,
        Conflict,
    }

    /// <summary>
    /// 写三态分类（**唯一判定点**）：缺失 → 新增；同实例 → 幂等；同逻辑类型（同类型 / 同名跨 ALC）→
    /// 跨代策略下重建覆盖、严格策略下冲突（DF8）；异类型 → 冲突。消除「Register / RegisterOrReplaceAcrossGeneration /
    /// GetOrAdd 重建」三处各写一遍的平行判定。
    /// </summary>
    private static SlotWriteOutcome ClassifyWrite(object? existing, object incoming, SlotWritePolicy policy)
    {
        if (existing is null)
            return SlotWriteOutcome.Added;
        if (ReferenceEquals(existing, incoming))
            return SlotWriteOutcome.Idempotent;

        if (IsSameLogicalType(existing.GetType(), incoming.GetType()))
            return policy == SlotWritePolicy.CrossGeneration ? SlotWriteOutcome.Replaced : SlotWriteOutcome.Conflict;

        return SlotWriteOutcome.Conflict;
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