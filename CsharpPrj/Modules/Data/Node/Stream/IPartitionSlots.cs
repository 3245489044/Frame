using System;

namespace Modules.Node.Stream;

/// <summary>
/// 分区槽端口（通用；宿主注入）—— 把「按实例键（run/task）分区承载的槽对象」抽象为端口，
/// 使 <see cref="AggregateRegistry{TRun}"/> 不依赖任何具体运行时槽实现（Agent 侧经此注入实例分区槽）。
/// <para>语义：同实例键幂等（首访装配、后续同实例）；<see cref="TryGet"/> 未命中**不装配**。</para>
/// </summary>
/// <typeparam name="TRun">槽对象类型（聚合运行容器）。</typeparam>
public interface IPartitionSlots<TRun> where TRun : class
{
    /// <summary>取（缺失则装配）该实例键的槽对象（同键幂等）。</summary>
    /// <param name="instanceKey">实例键（run / task 身份）。</param>
    /// <param name="factory">装配工厂（形参为实例键；仅首次解析该实例时调用）。</param>
    TRun GetOrAdd(string instanceKey, Func<string, TRun> factory);

    /// <summary>尝试取某实例键的槽对象（缺失返回 false，**不装配**）。</summary>
    /// <param name="instanceKey">实例键（run / task 身份）。</param>
    /// <param name="value">槽对象（未命中为 null）。</param>
    bool TryGet(string instanceKey, out TRun? value);
}
