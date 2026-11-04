using System;

namespace Modules.DeclarativeDataflow.Lease;

/// <summary>
/// 单槽（**单写者 + 后写胜出**）—— 具名租约的通用载体：只承载"当前一个值"，
/// 置位返回前值（供回滚/诊断），空值语义显式（<see cref="Has"/> 判存在，不用 null 冒充"未设置"）。
/// </summary>
/// <typeparam name="T">引用类型载荷。</typeparam>
public sealed class SingleSlot<T> where T : class
{
    /// <summary>当前值（未设置 ⇒ null）。</summary>
    public T? Value { get; private set; }

    /// <summary>是否已置位。</summary>
    public bool Has => Value is not null;

    /// <summary>置位；返回前值（null = 之前为空）。</summary>
    public T? Set(T? value)
    {
        var previous = Value;
        Value = value;
        return previous;
    }

    /// <summary>清空。</summary>
    public void Clear() => Value = null;
}
