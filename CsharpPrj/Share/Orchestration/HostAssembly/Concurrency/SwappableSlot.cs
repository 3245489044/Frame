using System;

namespace Share.HostAssembly.Concurrency;

/// <summary>
/// 可替换槽位（通用件）—— 承载可释放资源（<see cref="IDisposable"/>）的「装配即替换、替换释放旧值」生命周期：
/// 交换在锁内完成（槽位不出现空窗），旧值在锁外释放（避免释放回调重入锁）。
/// </summary>
/// <typeparam name="T">槽位承载类型（可释放引用类型）。</typeparam>
public sealed class SwappableSlot<T> where T : class, IDisposable
{
    private readonly object _gate;
    private T? _value;

    /// <param name="gate">共享锁（与同宿主其它槽位保持同一并发语义）；null ⇒ 自建锁。</param>
    public SwappableSlot(object? gate = null) => _gate = gate ?? new object();

    /// <summary>当前值；未装配为 null。</summary>
    public T? Value { get { lock (_gate) return _value; } }

    /// <summary>是否已装配。</summary>
    public bool Has { get { lock (_gate) return _value != null; } }

    /// <summary>装配（覆盖式）：返回新值；旧值按 <paramref name="disposePrevious"/> 释放（同引用不重复释放）。</summary>
    public T Attach(T value, bool disposePrevious = true)
    {
        PortGuard.Required(value, nameof(value));
        T? previous;
        lock (_gate)
        {
            previous = _value;
            _value = value;
        }

        if (disposePrevious && previous is not null && !ReferenceEquals(previous, value))
            previous.Dispose();
        return value;
    }

    /// <summary>解除装配（返回旧值，不释放；释放责任交给调用方）。</summary>
    public T? Detach()
    {
        lock (_gate)
        {
            var previous = _value;
            _value = null;
            return previous;
        }
    }
}
