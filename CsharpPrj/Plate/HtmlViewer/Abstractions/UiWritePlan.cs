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
}

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

    /// <summary>叶子定点写计划。</summary>
    public static UiWritePlan Leaf(string name, string value)
        => new(UiWriteKind.Leaf, new[] { new UiWriteTarget(name, value) });

    /// <summary>整文档写计划。</summary>
    public static UiWritePlan FullDocument(string html, string? fingerprint = null)
        => new(UiWriteKind.FullDocument, new[] { new UiWriteTarget(null, html) }, fingerprint);
}
