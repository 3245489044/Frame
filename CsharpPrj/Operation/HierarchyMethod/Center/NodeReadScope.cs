using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.HierarchyMethod.Center;

/// <summary>节点读源范围形态（受控词）。</summary>
public enum NodeReadScopeKind
{
    /// <summary>全量枚举（直跑 / 单测 / 诊断显式授权；生产轮禁用）。</summary>
    Full,

    /// <summary>按 task 归属节点键过滤（生产轮默认）。</summary>
    ByTask,

    /// <summary>按显式节点键集合有界读取（键集即上界）。</summary>
    ByKeys,
}

/// <summary>
/// 节点读源**范围声明**（F3 通用件）：把「读多少」从副作用（空 key ⇒ 全量）提升为调用点**显式**形态。
/// <para>
/// 纪律（AX.NoSilentOutlet / Def.CostClassBoundedness）：
/// ① <see cref="ByTask"/> 的 taskKey 必须非空白 —— 空白即构造期 fail-fast，**禁**静默降级为全量（ERR-01 根因）；
/// ② <see cref="Full"/> 只能显式构造，不因缺参数自动落到全量；
/// ③ <see cref="ByKeys"/> 的键集即量化上界；空集 ⇒ 空结果（不报错）。
/// </para>
/// </summary>
public sealed class NodeReadScope
{
    private static readonly IReadOnlyCollection<string> NoKeys = Array.Empty<string>();

    private NodeReadScope(NodeReadScopeKind kind, string? taskKey, IReadOnlyCollection<string> nodeKeys)
    {
        Kind = kind;
        TaskKey = taskKey;
        NodeKeys = nodeKeys;
    }

    /// <summary>全量（显式）。</summary>
    public static NodeReadScope Full { get; } = new(NodeReadScopeKind.Full, null, NoKeys);

    public NodeReadScopeKind Kind { get; }

    /// <summary>task 键（仅 <see cref="NodeReadScopeKind.ByTask"/> 有值）。</summary>
    public string? TaskKey { get; }

    /// <summary>节点键集合（仅 <see cref="NodeReadScopeKind.ByKeys"/> 有值；其余为空集）。</summary>
    public IReadOnlyCollection<string> NodeKeys { get; }

    /// <summary>按 task 归属读取（taskKey 空白 ⇒ 抛，禁降级全量）。</summary>
    public static NodeReadScope ByTask(string taskKey)
    {
        if (string.IsNullOrWhiteSpace(taskKey))
            throw new ArgumentException(
                "NodeReadScope.ByTask 需要非空 taskKey（身份缺失应在上游 fail-fast，禁降级为全量读）。",
                nameof(taskKey));
        return new NodeReadScope(NodeReadScopeKind.ByTask, taskKey, NoKeys);
    }

    /// <summary>按键集合有界读取（空白键被丢弃；空集 ⇒ 空结果）。</summary>
    public static NodeReadScope ByKeys(IReadOnlyCollection<string> nodeKeys)
    {
        ArgumentNullException.ThrowIfNull(nodeKeys);
        var keys = nodeKeys.Where(static key => !string.IsNullOrWhiteSpace(key)).ToArray();
        return new NodeReadScope(NodeReadScopeKind.ByKeys, null, keys);
    }

    public override string ToString() => Kind switch
    {
        NodeReadScopeKind.Full => "full",
        NodeReadScopeKind.ByTask => $"task:{TaskKey}",
        _ => $"keys:{NodeKeys.Count}",
    };
}
