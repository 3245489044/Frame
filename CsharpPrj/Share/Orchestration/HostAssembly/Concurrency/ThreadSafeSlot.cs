using System;

namespace Share.HostAssembly.Concurrency;

/// <summary>
/// 线程安全槽位容器 —— 封装「get/Has/Attach/Detach」的 lock 模板 + 空安全，
/// 消除各静态宿主散落的锁样板。共享同一把锁时保持并发语义一致。
/// </summary>
/// <typeparam name="T">槽位承载类型（引用类型）。</typeparam>
public sealed class ThreadSafeSlot<T> where T : class
{
    private readonly object _gate;
    private T? _value;

    public ThreadSafeSlot(object? gate = null) => _gate = gate ?? new object();

    /// <summary>当前值；未装配为 null。</summary>
    public T? Value { get { lock (_gate) return _value; } }

    /// <summary>是否已装配。</summary>
    public bool Has { get { lock (_gate) return _value != null; } }

    /// <summary>装配（幂等覆盖式）。传入需非 null。</summary>
    public void Attach(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate) _value = value;
    }

    /// <summary>解除装配（不释放其资源）。</summary>
    public void Detach() { lock (_gate) _value = null; }
}
