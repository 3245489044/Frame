#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>预算裁决结果：<paramref name="Kept"/> 必落地；<paramref name="Dropped"/> 本拍丢弃。</summary>
public readonly record struct BudgetDecision(
    IReadOnlyList<SectionPlan> Kept,
    IReadOnlyList<SectionPlan> Dropped,
    int Nodes,
    int Bytes,
    string? DegradeReason);

/// <summary>帧级预算裁决结果（目标粒度）：仅回答「哪些目标本拍不写」，内容取舍由节策略负责。</summary>
public readonly record struct TargetBudgetDecision(
    IReadOnlyList<UiWriteTarget> Kept,
    int Dropped,
    bool OverCapAfterKeep,
    string? DegradeReason);

/// <summary>
/// 每拍预算（以**节点数**为主预算，对齐设计 D4）：超限 ⇒ 先丢装饰（progressive）→ 再丢 volatile → 节点仍超才按体量裁 stable（至少留一节）→
/// 交互拍永不丢弃（只可见化降级原因）。每次降级必带 reason（禁静默）。
/// <para>**字节超限不整节丢弃**：字节只是辅助预算，丢整节会让界面缺块/停留旧 DOM，比慢更差；
/// 字节超限只记账（reason=bytes-over-budget）并整节落地。</para>
/// </summary>
public static class WriteBudgetPolicy
{
    /// <summary>
    /// 帧级预算（**时间/ops 上限**）：跳叶子 → 跳流段 → 多目标按体量裁剪 → 单目标超限仍落地（留陈旧 DOM 比慢更差）。
    /// <para>与 <see cref="Decide"/> 分工：本方法只回答「本拍写哪些目标」，不决定节内容取舍（单源 = 本节策略文件）。</para>
    /// </summary>
    public static TargetBudgetDecision DecideTargets(
        UiWritePlan plan,
        int maxOps,
        int maxBytes,
        Func<IReadOnlyList<UiWriteTarget>, int> estimateBytes,
        Func<UiWriteTarget, int> estimateTargetBytes)
    {
        var targets = plan.Targets;
        var ops = targets.Count;
        var bytes = estimateBytes(targets);
        if (ops <= maxOps && bytes <= maxBytes)
            return new TargetBudgetDecision(targets, 0, false, null);

        var kept = new List<UiWriteTarget>(targets.Count);
        var droppedLeaves = 0;
        var droppedStreams = 0;
        var leafPayload = plan.Kind is UiWriteKind.Leaf or UiWriteKind.Composite or UiWriteKind.TreeAndLeaf;
        foreach (var target in targets)
        {
            if (leafPayload && target.Payload is string) { droppedLeaves++; continue; }
            if (target.Payload is UiStreamAppend or UiStreamTrim) { droppedStreams++; continue; }
            kept.Add(target);
        }

        var reason = droppedLeaves > 0 ? "leaf-skipped" : droppedStreams > 0 ? "stream-skipped" : "over-budget";

        // 多目标仍越界 ⇒ 仅按**目标数**裁剪（至少保留一个，顺序按原计划保持）；
        // 字节超限不裁整目标：整目标丢弃会留下陈旧 DOM / 缺块，比慢更差（只记账 oversize）。
        if (kept.Count > 1 && kept.Count > maxOps)
        {
            var dropCandidates = new HashSet<UiWriteTarget>();
            var bySize = kept.OrderByDescending(estimateTargetBytes).ToList();
            while (bySize.Count > 1 && bySize.Count > maxOps)
            {
                dropCandidates.Add(bySize[0]);
                bySize.RemoveAt(0);
            }

            if (dropCandidates.Count > 0)
            {
                kept = kept.Where(target => !dropCandidates.Contains(target)).ToList();
                reason = "target-pruned";
            }
        }

        var dropped = ops - kept.Count;
        var overAfterKeep = kept.Count > maxOps || estimateBytes(kept) > maxBytes;
        return new TargetBudgetDecision(kept, dropped, overAfterKeep, reason);
    }

    public static BudgetDecision Decide(
        WriteBudget budget, IReadOnlyList<SectionPlan> sections, UiWriteSource source, bool progressive = true)
    {
        if (sections is null || sections.Count == 0)
            return new BudgetDecision(Array.Empty<SectionPlan>(), Array.Empty<SectionPlan>(), 0, 0, null);

        // 一次遍历：总量（后续只操作累加变量，禁重扫/重算 sections）。
        var nodes = 0;
        var bytes = 0;
        foreach (var section in sections)
        {
            nodes += Math.Max(0, section.Nodes);
            bytes += Math.Max(0, section.Bytes);
        }

        var overNodes = budget.MaxNodes > 0 && nodes > budget.MaxNodes;
        var overBytes = budget.MaxBytes > 0 && bytes > budget.MaxBytes;
        if (!overNodes && !overBytes)
            return new BudgetDecision(sections, Array.Empty<SectionPlan>(), nodes, bytes, null);

        // 交互永不丢弃：写丢交互比慢更差（判据可复算：degradeReason 记 interactive-over-budget）。
        if (source == UiWriteSource.Interactive)
            return new BudgetDecision(sections, Array.Empty<SectionPlan>(), nodes, bytes, "interactive-over-budget");

        // F5 渐进表面：预算压力下先丢「装饰」阶段（徽标/成本/易变叶子），壳/结构/内容保留。
        var kept = new List<SectionPlan>(sections.Count);
        var dropped = new List<SectionPlan>();
        var keptNodes = 0;
        var keptBytes = 0;
        foreach (var section in sections)
        {
            if (progressive && section.Stage == SurfaceStage.Decorations)
            {
                dropped.Add(section);
                continue;
            }

            kept.Add(section);
            keptNodes += Math.Max(0, section.Nodes);
            keptBytes += Math.Max(0, section.Bytes);
        }

        var reason = overNodes ? "nodes-over-budget" : "bytes-over-budget";
        if (dropped.Count > 0) reason = "decorations-over-budget";

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

        // 2) 节点仍超 ⇒ 按体量从大到小裁 stable（至少留一节，禁整批丢光）。
        //    仅节点预算是硬约束；字节超限不整节丢弃（见类型注释）。
        while (kept.Count > 1 && budget.MaxNodes > 0 && keptNodes > budget.MaxNodes)
        {
            var largest = 0;
            for (var i = 1; i < kept.Count; i++)
            {
                if (kept[i].Nodes > kept[largest].Nodes) largest = i;
            }

            var victim = kept[largest];
            kept.RemoveAt(largest);
            dropped.Add(victim);
            keptNodes -= Math.Max(0, victim.Nodes);
            keptBytes -= Math.Max(0, victim.Bytes);
            reason = "nodes-pruned";
        }

        // 3) 单节仍超：保留落地（留陈旧 DOM 比慢更差）并可见化。
        overNodes = budget.MaxNodes > 0 && keptNodes > budget.MaxNodes;
        overBytes = budget.MaxBytes > 0 && keptBytes > budget.MaxBytes;
        if (overNodes || overBytes)
        {
            reason = overNodes ? "oversize-single-section" : "bytes-over-budget-kept";
        }

        return new BudgetDecision(kept, dropped, keptNodes, keptBytes, reason);
    }
}
