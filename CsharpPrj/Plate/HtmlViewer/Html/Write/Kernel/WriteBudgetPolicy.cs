#nullable enable

using System;
using System.Collections.Generic;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>预算裁决结果：<paramref name="Kept"/> 必落地；<paramref name="Dropped"/> 本拍丢弃（后台可 defer）。</summary>
public readonly record struct BudgetDecision(
    IReadOnlyList<SectionPlan> Kept,
    IReadOnlyList<SectionPlan> Dropped,
    int Nodes,
    int Bytes,
    string? DegradeReason,
    bool Deferred);

/// <summary>
/// 每拍预算（以**节点数**为主预算，对齐设计 D4）：超限 ⇒ 先丢 volatile 节 → 再按体量裁 stable（至少留一节）→
/// 交互拍永不丢弃（只可见化降级原因）。每次降级必带 reason（禁静默）。
/// </summary>
public static class WriteBudgetPolicy
{
    public static BudgetDecision Decide(WriteBudget budget, IReadOnlyList<SectionPlan> sections, UiWriteSource source)
    {
        if (sections is null || sections.Count == 0)
            return new BudgetDecision(Array.Empty<SectionPlan>(), Array.Empty<SectionPlan>(), 0, 0, null, false);

        var nodes = 0;
        var bytes = 0;
        foreach (var s in sections)
        {
            nodes += Math.Max(0, s.Nodes);
            bytes += Math.Max(0, s.Bytes);
        }

        var overNodes = budget.MaxNodes > 0 && nodes > budget.MaxNodes;
        var overBytes = budget.MaxBytes > 0 && bytes > budget.MaxBytes;
        if (!overNodes && !overBytes)
            return new BudgetDecision(sections, Array.Empty<SectionPlan>(), nodes, bytes, null, false);

        // 交互永不 defer/丢弃：写丢交互比慢更差（判据可复算：degradeReason 记 interactive-over-budget）。
        if (source == UiWriteSource.Interactive)
            return new BudgetDecision(sections, Array.Empty<SectionPlan>(), nodes, bytes, "interactive-over-budget", false);

        var kept = new List<SectionPlan>(sections.Count);
        var dropped = new List<SectionPlan>();
        var keptNodes = 0;
        var keptBytes = 0;
        var reason = overNodes ? "nodes-over-budget" : "bytes-over-budget";

        // 1) 先丢 volatile（tick/stream）。
        foreach (var s in sections)
        {
            if (s.Variance == SectionVariance.Volatile)
            {
                dropped.Add(s);
                continue;
            }

            kept.Add(s);
            keptNodes += Math.Max(0, s.Nodes);
            keptBytes += Math.Max(0, s.Bytes);
        }

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

        return new BudgetDecision(kept, dropped, keptNodes, keptBytes, reason, false);
    }
}
