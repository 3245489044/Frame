#nullable enable

using System;
using System.Collections.Generic;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>预算裁决结果：<paramref name="Kept"/> 必落地；<paramref name="Dropped"/> 本拍丢弃。</summary>
public readonly record struct BudgetDecision(
    IReadOnlyList<SectionPlan> Kept,
    IReadOnlyList<SectionPlan> Dropped,
    int Nodes,
    int Bytes,
    string? DegradeReason);

/// <summary>
/// 每拍预算（以**节点数**为主预算，对齐设计 D4）：超限 ⇒ 先丢 volatile 节 → 再按体量裁 stable（至少留一节）→
/// 交互拍永不丢弃（只可见化降级原因）。每次降级必带 reason（禁静默）。
/// </summary>
public static class WriteBudgetPolicy
{
    public static BudgetDecision Decide(
        WriteBudget budget, IReadOnlyList<SectionPlan> sections, UiWriteSource source, bool progressive = true)
    {
        if (sections is null || sections.Count == 0)
            return new BudgetDecision(Array.Empty<SectionPlan>(), Array.Empty<SectionPlan>(), 0, 0, null);

        // 一次遍历：总量 + 阶段分类同时完成（后续只操作累加变量，禁重扫/重算 sections）。
        var nodes = 0;
        var bytes = 0;
        var kept = new List<SectionPlan>(sections.Count);
        var dropped = new List<SectionPlan>();
        var keptNodes = 0;
        var keptBytes = 0;
        foreach (var section in sections)
        {
            var sectionNodes = Math.Max(0, section.Nodes);
            var sectionBytes = Math.Max(0, section.Bytes);
            nodes += sectionNodes;
            bytes += sectionBytes;

            // F5 渐进表面：预算压力下先丢「装饰」阶段（徽标/成本/易变叶子），壳/结构/内容保留。
            if (progressive && section.Stage == SurfaceStage.Decorations)
            {
                dropped.Add(section);
                continue;
            }

            kept.Add(section);
            keptNodes += sectionNodes;
            keptBytes += sectionBytes;
        }

        var overNodes = budget.MaxNodes > 0 && nodes > budget.MaxNodes;
        var overBytes = budget.MaxBytes > 0 && bytes > budget.MaxBytes;
        if (!overNodes && !overBytes)
            return new BudgetDecision(sections, Array.Empty<SectionPlan>(), nodes, bytes, null);

        // 交互永不丢弃：写丢交互比慢更差（判据可复算：degradeReason 记 interactive-over-budget）。
        if (source == UiWriteSource.Interactive)
            return new BudgetDecision(sections, Array.Empty<SectionPlan>(), nodes, bytes, "interactive-over-budget");

        var reason = overNodes ? "nodes-over-budget" : "bytes-over-budget";
        if (progressive && dropped.Count > 0) reason = "decorations-over-budget";

        overNodes = budget.MaxNodes > 0 && keptNodes > budget.MaxNodes;
        overBytes = budget.MaxBytes > 0 && keptBytes > budget.MaxBytes;
        if (!overNodes && !overBytes)
            return new BudgetDecision(kept, dropped, keptNodes, keptBytes, reason);

        // 1) 再丢 volatile（tick/stream）；一次遍历构建 stable 并累加。
        var stable = new List<SectionPlan>(kept.Count);
        var stableNodes = 0;
        var stableBytes = 0;
        foreach (var section in kept)
        {
            if (section.Variance == SectionVariance.Volatile)
            {
                dropped.Add(section);
                continue;
            }
            stable.Add(section);
            stableNodes += Math.Max(0, section.Nodes);
            stableBytes += Math.Max(0, section.Bytes);
        }
        kept = stable;
        keptNodes = stableNodes;
        keptBytes = stableBytes;

        // 2) 仍超 ⇒ 按体量从大到小裁 stable（至少留一节，禁整批丢光）。
        while (kept.Count > 1
               && ((budget.MaxNodes > 0 && keptNodes > budget.MaxNodes)
                   || (budget.MaxBytes > 0 && keptBytes > budget.MaxBytes)))
        {
            var largest = 0;
            for (var i = 1; i < kept.Count; i++)
            {
                if (kept[i].Bytes > kept[largest].Bytes) largest = i;
            }

            var victim = kept[largest];
            kept.RemoveAt(largest);
            dropped.Add(victim);
            keptNodes -= Math.Max(0, victim.Nodes);
            keptBytes -= Math.Max(0, victim.Bytes);
            reason = "stable-pruned";
        }

        // 3) 单节仍超：保留落地（留陈旧 DOM 比慢更差）并可见化。
        if (kept.Count == 1
            && ((budget.MaxNodes > 0 && keptNodes > budget.MaxNodes)
                || (budget.MaxBytes > 0 && keptBytes > budget.MaxBytes)))
        {
            reason = "oversize-single-section";
        }

        return new BudgetDecision(kept, dropped, keptNodes, keptBytes, reason);
    }
}
