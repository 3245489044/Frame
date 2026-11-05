using System;
using Modules.DeclarativeDataflow.Budget;

namespace Modules.DeclarativeDataflow.Processing;

/// <summary>多层遍历方向。</summary>
public enum HierarchyTraversalOrder
{
    /// <summary>自顶向下：先处理本节点，再把本节点结论作为下游下传并展开其子层（父决定子）。</summary>
    TopDown,

    /// <summary>自底向上：先处理全部子层，再把子层结论交给本节点聚合（子结论收敛到父）。</summary>
    BottomUp,
}

/// <summary>
/// 一轮多层处理的计划（外置：方向 / 预算 / 处理器覆盖 / 展开止层）。
/// <para>
/// <see cref="ProcessIdOverride"/> 非空时覆盖各层声明的默认处理器，覆盖作用于**全部层**
/// （含原本无处理器的容器层——其"透传"默认语义会被覆盖）；需要按层分别处理时，请分层多次调用。
/// </para>
/// <para>
/// <see cref="StopLevelId"/> 指定"展开到该层为止"（该层仍被处理，但不展开其子层）。
/// </para>
/// </summary>
/// <param name="Order">遍历方向（缺省自底向上，便于子结论先收敛）。</param>
/// <param name="Budget">预算（节点数 + 深度）；Null 时取 <see cref="ProcessBudget.Default"/>，不定深度必配。</param>
/// <param name="ProcessIdOverride">覆盖处理器 Id（Null = 用各层声明；非 Null 时作用于全部层）。</param>
/// <param name="StopLevelId">展开止层（Null = 按 schema 展开到底）。</param>
public sealed record HierarchyProcessPlan(
    HierarchyTraversalOrder Order = HierarchyTraversalOrder.BottomUp,
    ProcessBudget? Budget = null,
    string? ProcessIdOverride = null,
    string? StopLevelId = null)
{
    /// <summary>缺省计划（自底向上 + 缺省有限预算）。</summary>
    public static HierarchyProcessPlan Default { get; } = new();

    /// <summary>生效预算（未显式给出时取 <see cref="ProcessBudget.Default"/>，绝不无界）。</summary>
    public ProcessBudget EffectiveBudget => Budget ?? ProcessBudget.Default;

    /// <summary>自顶向下计划。</summary>
    public static HierarchyProcessPlan TopDown(ProcessBudget? budget = null, string? processIdOverride = null, string? stopLevelId = null)
        => Default with
        {
            Order = HierarchyTraversalOrder.TopDown,
            Budget = budget,
            ProcessIdOverride = processIdOverride,
            StopLevelId = stopLevelId,
        };

    /// <summary>自底向上计划。</summary>
    public static HierarchyProcessPlan BottomUp(ProcessBudget? budget = null, string? processIdOverride = null, string? stopLevelId = null)
        => Default with
        {
            Order = HierarchyTraversalOrder.BottomUp,
            Budget = budget,
            ProcessIdOverride = processIdOverride,
            StopLevelId = stopLevelId,
        };
}
