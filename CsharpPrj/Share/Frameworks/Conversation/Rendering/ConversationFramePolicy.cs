#nullable enable

using System;
using System.Collections.Generic;
using Modules.Node.Frame;
using Share.Conversation.Presentation;

namespace Share.Conversation.Rendering;

/// <summary>分组/摘要文案与格式（宿主经 loc 声明注入；未注入取零领域词的 ASCII 默认）。</summary>
public sealed record FrameGroupLabels(
    string Lead,
    string Iteration,
    string Trailer,
    string Trace,
    string PostTrailer,
    string Other,
    string SessionScope = "session",
    string RoundTitleFormat = "round {0}",
    string OperationPanelTitle = "operations",
    string LeadSummaryFormat = "{0} · {1} items",
    string AttachLabel = "process",
    string AttachSummaryFormat = "{0} · {1} items ({2})",
    string AttachGroupCountFormat = "{0} {1}",
    string AttachSummaryFramesFormat = " · {0} frames",
    string AttachSummaryTextFormat = " · {0} text",
    string PostTrailerFormat = "{0} (post)",
    string DegradedFormat = "render degraded: {0}")
{
    /// <summary>通用默认（零领域词；领域标签/中文由宿主覆盖）。</summary>
    public static readonly FrameGroupLabels Default =
        new("lead", "iteration {0}", "trailer", "trace", "post", "other");
}

/// <summary>
/// 帧语义策略（**唯一注入面**）：分类（kind→角色标识/徽标）+ 分组文案 + 呈现声明。
/// <para>
/// 结构词（<see cref="FrameRoleIds"/>）与序列计划（<see cref="FrameSequencer{T}"/>）已下沉 <c>Modules.Node.Frame</c>；
/// 本类只把「产品声明」绑定成框架可直接消费的三元组。
/// </para>
/// </summary>
/// <param name="Kinds">kind → 角色/徽标（组内分组与徽标；不决定呈现身份）。</param>
/// <param name="Labels">分组文案。</param>
/// <param name="Presentation">呈现声明（产品策略：类清单 + 事实映射；框架零词表）。</param>
public sealed record FramePolicy(
    IReadOnlyDictionary<string, FrameRoleSpec> Kinds,
    FrameGroupLabels Labels,
    PresentationDeclaration Presentation)
{
    /// <summary>未登记 kind ⇒ 普通帧（可见、不丢弃）。</summary>
    public string RoleOf(string? kind)
        => kind is not null && Kinds.TryGetValue(kind, out var spec) ? spec.RoleId : FrameRoleIds.Plain;

    /// <summary>未登记徽标 ⇒ null（模板回退显示 kind）。</summary>
    public string? BadgeOf(string? kind)
        => kind is not null && Kinds.TryGetValue(kind, out var spec) ? spec.Badge : null;

    /// <summary>是否过程文本（可用于组内计数与角标；不决定呈现身份）。</summary>
    public bool IsTrace(string? kind) => RoleOf(kind) == FrameRoleIds.Trace;
}
