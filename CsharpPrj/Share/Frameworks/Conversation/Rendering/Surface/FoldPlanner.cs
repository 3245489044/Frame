#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Share.Conversation.Presentation;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 折叠计划器（**唯一折叠实现**）：语义层只产 <see cref="SurfaceRoleIds.Fold"/> 节点，
/// 不出现 details/summary/FoldPanel/字形。
/// <para>
/// 输入 = 合成计划 + 声明策略 + 段级轮次事实；输出 = 折叠后的计划（结构/计数不变）。
/// 载体负责把 Fold 节点投影为自己的折叠控件；未声明折叠 ⇒ 原样返回（零行为变化）。
/// </para>
/// </summary>
public static class FoldPlanner
{
    /// <summary>折叠节点数（递归；供门禁/Headless 断言）。</summary>
    public static int Count(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return CountNodes(plan.Roots);
    }

    /// <summary>应用折叠：组节点 → Fold，轮段 → 轮折叠（最新轮按策略打开）。</summary>
    /// <param name="plan">合成计划。</param>
    /// <param name="policy">折叠策略（null / None ⇒ 不折叠）。</param>
    /// <param name="round">段轮次（0 = 非轮段）。</param>
    /// <param name="latestRound">本段是否最新轮（策略 OpenLatestRound 用）。</param>
    public static RenderPlan Apply(RenderPlan plan, FoldPolicy? policy, int round = 0, bool latestRound = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (policy is null || policy.Mode == RoundFoldMode.None)
        {
            return plan;
        }

        var roots = Decorate(plan.Roots, policy);
        if (round > 0)
        {
            var open = policy.IsOpen(round, latestRound ? round : round + 1);
            var label = new UiField(
                "round",
                SurfaceCellIds.Badge,
                round.ToString(CultureInfo.InvariantCulture),
                "round");
            roots = new UiSurfaceNode[]
            {
                new(
                    SurfaceRoleIds.Fold,
                    $"fold:round:{round.ToString(CultureInfo.InvariantCulture)}",
                    PresentationSurface.Process,
                    "round",
                    new[] { label },
                    roots,
                    open ? UiNodeState.Open : UiNodeState.Closed,
                    new[] { "toggleFold" }),
            };
        }

        return new RenderPlan(plan.ViewKey, roots) { Counts = plan.Counts };
    }

    private static IReadOnlyList<UiSurfaceNode> Decorate(IReadOnlyList<UiSurfaceNode> nodes, FoldPolicy policy)
    {
        if (nodes.Count == 0) return nodes;
        var list = new List<UiSurfaceNode>(nodes.Count);
        foreach (var node in nodes)
        {
            var children = Decorate(node.Children, policy);
            if (string.Equals(node.Role, SurfaceRoleIds.Group, StringComparison.Ordinal))
            {
                // 组默认收起（中层可折叠）；轮段由 Apply 统一包装。
                list.Add(node with
                {
                    Role = SurfaceRoleIds.Fold,
                    Children = children,
                    State = UiNodeState.Closed,
                    Actions = node.Actions ?? new[] { "toggleFold" },
                });
                continue;
            }

            list.Add(children == node.Children ? node : node with { Children = children });
        }

        return list;
    }

    private static int CountNodes(IReadOnlyList<UiSurfaceNode> nodes)
    {
        var count = 0;
        foreach (var node in nodes)
        {
            if (string.Equals(node.Role, SurfaceRoleIds.Fold, StringComparison.Ordinal)) count++;
            count += CountNodes(node.Children);
        }

        return count;
    }
}
