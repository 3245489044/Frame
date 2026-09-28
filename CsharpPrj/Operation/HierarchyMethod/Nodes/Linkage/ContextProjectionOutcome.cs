using System.Collections.Generic;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>
/// 上下文投影结论（框架中立）—— 读投影流的产物面：
/// 成功 ⇒ <see cref="Ports"/> = 该流 <c>outports=</c> 的键值表（如 <c>context_messages</c>）；
/// 失败 / 未装配 ⇒ <see cref="Reason"/> 逐字归因（调用方据此可见化或回退，禁静默）。
/// </summary>
/// <param name="Ok">true = 已提交（产物可读）；false = 未装配执行器 / 流失败 / 回收缺失。</param>
/// <param name="Ports">产物键值表（Ok=false 时为 null）。</param>
/// <param name="Reason">人读归因（Ok=true 时为空串）。</param>
public readonly record struct ContextProjectionOutcome(
    bool Ok,
    IReadOnlyDictionary<string, object?>? Ports,
    string? Reason)
{
    /// <summary>已提交（产物可读）。</summary>
    /// <param name="ports">该流 outports= 的键值表。</param>
    public static ContextProjectionOutcome Committed(IReadOnlyDictionary<string, object?> ports)
        => new(true, ports, null);

    /// <summary>未完成（未装配 / 流失败 / 回收缺失）。</summary>
    /// <param name="reason">人读归因。</param>
    public static ContextProjectionOutcome Failed(string reason) => new(false, null, reason);
}
