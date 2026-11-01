#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Share.Conversation.Presentation;

namespace Share.Conversation.Rendering;

/// <summary>
/// 帧角色标识受控词（唯一语义轴）：分组、终端提升、徽标与配对全部由本轴派生。
/// <para>取值是字符串标识（非枚举）：角色语义由宿主声明面给出（kind→RoleId），
/// 通用渲染器只做机械匹配，不认识任何领域角色名。</para>
/// </summary>
public static class FrameRoleIds
{
    /// <summary>普通帧（逐条成行）。</summary>
    public const string Plain = "plain";

    /// <summary>过程文本（镜像，不冒充终端实体；不参与呈现身份判定）。</summary>
    public const string Trace = "trace";

    /// <summary>终端实体（权威源唯一；只有该角色可升为终端气泡）。</summary>
    public const string Terminal = "terminal";

    /// <summary>边界帧（开新顺序组）。</summary>
    public const string Boundary = "boundary";

    /// <summary>收束帧（结算组；只接收本角色，不吞后续异类帧）。</summary>
    public const string Trailer = "trailer";

    /// <summary>调用帧（可被结果帧配对嵌套）。</summary>
    public const string Call = "call";

    /// <summary>结果帧（嵌套到最近调用行；无配对则独立成行，零丢失）。</summary>
    public const string Result = "result";

    /// <summary>操作帧（非气泡：进顶部操作面板；通道由呈现身份决定）。</summary>
    public const string Operation = "operation";
}

/// <summary>kind → 角色标识 + 徽标（声明驱动的单表条目；宿主注入）。</summary>
public sealed record FrameRoleSpec(string Kind, string RoleId, string? Badge);

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
    public static readonly FrameGroupLabels Default = new("lead", "iteration {0}", "trailer", "trace", "post", "other");
}

/// <summary>
/// 帧语义策略（单一注入面）：分类（kind→角色标识/徽标）+ 分组文案 + 终端身份 + 气泡锚点扩展规则。
/// <para>渲染器只读本表，不再读 <see cref="AttachClassification"/> 的组序或 <see cref="AttachGrouping"/> 的多集合。</para>
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
