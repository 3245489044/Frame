using Modules.DeclarativeDataflow.Budget;
using Modules.DeclarativeDataflow.Processing;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 处理编排字段的**唯一解析实现**（<c>order=</c> / <c>budget=</c>）：闸A（对非法值报 `US1018`）与计划发射层
/// （取回退值）共用同一实现，避免「校验一处、发射一处」的双份回退规则漂移（<c>Strategy.SingleImplementation</c>）。
/// <para>
/// 回退口径：未知 <c>order=</c> → <c>BottomUp</c>；非法 <c>budget=</c> → 无显式预算（引擎侧取
/// <c>HierarchyProcessPlan.EffectiveBudget</c> 的缺省有限预算，绝不无界）。
/// </para>
/// </summary>
public static class UsharpPlanFields
{
    /// <summary>合法遍历方向取值（书写面单一来源 = <see cref="UsharpLanguage"/>）。</summary>
    public static IReadOnlyList<string> Orders { get; } =
        new[] { UsharpLanguage.OrderBottomUp, UsharpLanguage.OrderTopDown };

    /// <summary>
    /// 解析 <c>order=</c>：缺省 / 合法值 → true；**在场但未知** → false（调用方报 `US1018`）。
    /// <paramref name="order"/> 恒为可用的回退值（未知值按 <c>BottomUp</c> 处理）。
    /// </summary>
    public static bool TryOrder(UsharpFlowDecl flow, out HierarchyTraversalOrder order)
    {
        ArgumentNullException.ThrowIfNull(flow);

        order = HierarchyTraversalOrder.BottomUp;
        var raw = flow.Order;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        if (string.Equals(raw, UsharpLanguage.OrderTopDown, StringComparison.OrdinalIgnoreCase))
        {
            order = HierarchyTraversalOrder.TopDown;
            return true;
        }
        if (string.Equals(raw, UsharpLanguage.OrderBottomUp, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// 解析 <c>budget=</c>：缺省 / 合法 → true；**在场但非法** → false（调用方报 `US1018`），此时
    /// <paramref name="budget"/> 为 null（不静默兜底造预算）。
    /// </summary>
    public static bool TryBudget(UsharpFlowDecl flow, out ProcessBudget? budget)
    {
        ArgumentNullException.ThrowIfNull(flow);

        budget = flow.Budget is { } parsed ? new ProcessBudget(parsed.MaxNodes, parsed.MaxDepth) : null;
        return flow.BudgetRaw is null || flow.Budget is not null;
    }
}
