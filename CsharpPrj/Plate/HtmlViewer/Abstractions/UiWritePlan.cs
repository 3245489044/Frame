#nullable enable

using System;
using System.Collections.Generic;

namespace UiBackend.Abstractions;

/// <summary>写回计划种类（L1 通用契约；收敛整页/区域/键控树/叶子四通道，替代各后端各自为政的写分支）。</summary>
public enum UiWriteKind
{
    /// <summary>不写（显式空计划，取代哨兵串）。</summary>
    Skip = 0,
    /// <summary>区域写（按具名区域根；payload = HTML 片段，由写原语键控对齐）。</summary>
    Region,
    /// <summary>键控节点树写（payload = <see cref="UiNode"/>，经对齐原语落地）。</summary>
    Tree,
    /// <summary>叶子定点写（payload = 值；仅更新命中单元素）。</summary>
    Leaf,
    /// <summary>整文档写（首挂载/显式策略，非隐式回退）。</summary>
    FullDocument,

    /// <summary>键控树 + 易变叶子合并写（同拍：区域树先落地，叶子后定点更新；payload 按 <see cref="UiNode"/>/string 区分）。</summary>
    TreeAndLeaf,

    /// <summary>流追加（append-only 面：日志 / 过程帧 / 消息气泡；payload = <see cref="UiStreamAppend"/>）。
    /// 语义 = 只增不重建：按项 key 定点更新同名项，其余插入流首，并按 cap 裁尾。</summary>
    Append,

    /// <summary>流裁剪（保留流首 N 项；payload = <see cref="UiStreamTrim"/>）。</summary>
    Trim,
}

/// <summary>流项（append-only 面单条）：<paramref name="Key"/> 为稳定键（同键 ⇒ 定点更新，不新增行）。</summary>
public sealed record UiStreamItem(string Key, string Html);

/// <summary>流追加载荷：<paramref name="Items"/> 按「新→旧」序（流首为最新）；<paramref name="Cap"/> 为流长度上限（null/≤0 = 不裁）。</summary>
public sealed record UiStreamAppend(IReadOnlyList<UiStreamItem> Items, int? Cap = null);

/// <summary>流裁剪载荷：保留流首 <paramref name="Keep"/> 项（≤0 = 不裁）。</summary>
public sealed record UiStreamTrim(int Keep);

/// <summary>单条写回目标（Region/Tree/Leaf 用具名 Name；FullDocument 用 null）。Payload 形态随 Kind：Region=string / Tree=UiNode / Leaf=string / FullDocument=string。</summary>
public readonly record struct UiWriteTarget(string? Name, object? Payload);

/// <summary>
/// 写回计划（L1 通用契约）——业务/渲染侧唯一产出形态；由写协调器（单写者）独占执行，产出 <see cref="UiWriteResult"/>。
/// <paramref name="Interaction"/> 为键控对齐后回放的交互态（滚动/焦点）；<paramref name="TraceId"/> 为刷新链路追踪号（一次刷新一条轨迹）。
/// </summary>
public sealed record UiWritePlan(
    UiWriteKind Kind,
    IReadOnlyList<UiWriteTarget> Targets,
    string? Fingerprint = null,
    UiInteractionSnapshot? Interaction = null,
    string? TraceId = null)
{
    /// <summary>不写（显式空计划）。</summary>
    public static UiWritePlan Skip { get; } = new(UiWriteKind.Skip, Array.Empty<UiWriteTarget>());

    /// <summary>单区域计划（HTML 片段写作载荷）。</summary>
    public static UiWritePlan Region(string regionName, string html, string? fingerprint = null)
        => new(UiWriteKind.Region, new[] { new UiWriteTarget(regionName, html) }, fingerprint);

    /// <summary>多区域计划。</summary>
    public static UiWritePlan Regions(IReadOnlyList<UiWriteTarget> regions, string? fingerprint = null)
        => new(UiWriteKind.Region, regions, fingerprint);

    /// <summary>键控节点树计划（单区域）。</summary>
    public static UiWritePlan Tree(string regionName, UiNode tree, string? fingerprint = null)
        => new(UiWriteKind.Tree, new[] { new UiWriteTarget(regionName, tree) }, fingerprint);

    /// <summary>键控节点树计划（多区域；每目标 payload = <see cref="UiNode"/>）。</summary>
    public static UiWritePlan Trees(IReadOnlyList<UiWriteTarget> trees, UiInteractionSnapshot? interaction = null, string? fingerprint = null, string? traceId = null)
        => new(UiWriteKind.Tree, trees, fingerprint, interaction, traceId);

    /// <summary>带追踪号的键控节点树计划（多区域）。</summary>
    public static UiWritePlan TreesTraced(IReadOnlyList<UiWriteTarget> trees, UiInteractionSnapshot? interaction, string? traceId)
        => new(UiWriteKind.Tree, trees, null, interaction, traceId);

    /// <summary>键控节点树 + 易变叶子合并计划（同拍两通道：树先落地、叶子后定点；叶子不触发区域重渲染）。</summary>
    public static UiWritePlan TreesAndLeaves(
        IReadOnlyList<UiWriteTarget> trees, IReadOnlyList<UiWriteTarget> leaves,
        UiInteractionSnapshot? interaction = null, string? traceId = null)
    {
        var targets = new List<UiWriteTarget>(trees.Count + leaves.Count);
        targets.AddRange(trees);
        targets.AddRange(leaves);
        return new(UiWriteKind.TreeAndLeaf, targets, null, interaction, traceId);
    }

    /// <summary>叶子定点写计划。</summary>
    public static UiWritePlan Leaf(string name, string value)
        => new(UiWriteKind.Leaf, new[] { new UiWriteTarget(name, value) });

    /// <summary>多点叶子定点写计划（替换易变叶子值，不重渲染区域）。</summary>
    public static UiWritePlan Leaves(IReadOnlyList<UiWriteTarget> leaves, UiInteractionSnapshot? interaction = null, string? traceId = null)
        => new(UiWriteKind.Leaf, leaves, null, interaction, traceId);

    /// <summary>流追加计划（append-only 面单点落地；<paramref name="items"/> 按「新→旧」序）。</summary>
    public static UiWritePlan Append(string stream, IReadOnlyList<UiStreamItem> items, int? cap = null)
        => new(UiWriteKind.Append, new[] { new UiWriteTarget(stream, new UiStreamAppend(items, cap)) });

    /// <summary>流裁剪计划（保留流首 <paramref name="keep"/> 项）。</summary>
    public static UiWritePlan Trim(string stream, int keep)
        => new(UiWriteKind.Trim, new[] { new UiWriteTarget(stream, new UiStreamTrim(keep)) });

    /// <summary>整文档写计划。</summary>
    public static UiWritePlan FullDocument(string html, string? fingerprint = null)
        => new(UiWriteKind.FullDocument, new[] { new UiWriteTarget(null, html) }, fingerprint);
}
