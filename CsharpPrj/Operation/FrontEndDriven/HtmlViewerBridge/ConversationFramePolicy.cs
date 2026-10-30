#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using Share.Conversation.Rendering;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 帧角色标识受控词（**唯一语义轴**）：分组、终端提升、徽标与配对全部由本轴派生。
/// <para>取值是**字符串标识**（非枚举）：角色语义由宿主声明面给出（kind→RoleId），
/// 通用渲染器只做机械匹配，不认识任何领域角色名（终答/LLM 回合等由 Agent 插件声明）。</para>
/// </summary>
public static class FrameRoleIds
{
    /// <summary>普通帧（逐条成行）。</summary>
    public const string Plain = "plain";

    /// <summary>文本锚点（归档消息，顶部气泡）。</summary>
    public const string Text = "text";

    /// <summary>过程文本（镜像，不冒充终端实体；可参与“本轮最后一段文本”的过渡提升）。</summary>
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
}

/// <summary>kind → 角色标识 + 徽标（声明驱动的单表条目；宿主注入）。</summary>
public sealed record FrameRoleSpec(string Kind, string RoleId, string? Badge);

/// <summary>终端提升策略（通用流呈现开关；终端实体落地后切 <see cref="EntityOnly"/>）。</summary>
public enum TerminalPromotion
{
    /// <summary>不提升（全部按过程文本呈现）。</summary>
    None = 0,

    /// <summary>过渡：本轮最后一条过程文本提升为终端气泡（带“未归档”角标）。</summary>
    LastTraceInRound = 1,

    /// <summary>只认终端实体（终端角色）；禁以最后一条过程文本冒充终端实体。</summary>
    EntityOnly = 2,
}

/// <summary>终端身份策略：提升模式 + 终端 kind 集 + 文本归一化（去重单源）。</summary>
public sealed record TerminalPolicy(TerminalPromotion Promotion, IReadOnlySet<string> TerminalKinds)
{
    /// <summary>不提升/不识别终端 kind。</summary>
    public static readonly TerminalPolicy None =
        new(TerminalPromotion.None, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>去重比较用归一化：trim + 空白折叠（E9：近似重复不得生成两个气泡）。</summary>
    public string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }
}

/// <summary>
/// 分组/摘要**文案与格式**（宿主经 loc 声明注入；未注入取**零领域词**的 ASCII 默认）。
/// <para>纪律：桥内禁出现领域中文与拼接式文案 —— 一切可读文本由宿主注入；
/// 带占位的格式串用 <see cref="string.Format(IFormatProvider, string, object[])"/> 组装（不拼串）。</para>
/// </summary>
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
/// 帧语义策略（**单一注入面**）：分类（kind→角色标识/徽标）+ 分组文案 + 终端身份
/// + **气泡锚点扩展规则**（宿主声明；框架零领域词）。
/// 渲染器只读本表，不再读 <see cref="AttachClassification"/> 的组序或 <see cref="AttachGrouping"/> 的多集合。
/// </summary>
/// <param name="Kinds">kind → 角色/徽标。</param>
/// <param name="Labels">分组文案。</param>
/// <param name="Terminal">终端身份策略。</param>
/// <param name="ExtraBubbleAnchor">
/// 额外的**气泡锚点**判定（宿主注入；默认 null）。框架默认锚点 = 对话通道的角色锚点
/// （<see cref="ConversationAnchors.IsAnchor(ConversationRenderItem)"/>）；宿主可在此声明
/// 「过程帧中哪些也算气泡」（如“角色=Assistant 且 kind=某文本类”）。框架不内置任何领域 kind 判定。
/// </param>
public sealed record FramePolicy(
    IReadOnlyDictionary<string, FrameRoleSpec> Kinds,
    FrameGroupLabels Labels,
    TerminalPolicy Terminal,
    Func<ConversationRenderItem, bool>? ExtraBubbleAnchor = null)
{
    /// <summary>未登记 kind ⇒ 普通帧（可见、不丢弃）。</summary>
    public string RoleOf(string? kind)
        => kind is not null && Kinds.TryGetValue(kind, out var spec) ? spec.RoleId : FrameRoleIds.Plain;

    /// <summary>未登记徽标 ⇒ null（模板回退显示 kind）。</summary>
    public string? BadgeOf(string? kind)
        => kind is not null && Kinds.TryGetValue(kind, out var spec) ? spec.Badge : null;

    /// <summary>是否过程文本（可参与过渡提升）。</summary>
    public bool IsTrace(string? kind) => RoleOf(kind) == FrameRoleIds.Trace;

    /// <summary>气泡锚点（框架默认 ∪ 宿主扩展规则）：锚点形成气泡，其余帧一律折叠。</summary>
    /// <param name="item">渲染项。</param>
    public bool IsBubbleAnchor(ConversationRenderItem item)
        => ConversationAnchors.IsAnchor(item) || (ExtraBubbleAnchor?.Invoke(item) ?? false);

    /// <summary>
    /// 旧选项适配（单批次兼容）：把 <see cref="AttachGrouping"/> + <see cref="AttachClassification"/>
    /// 转成单表策略；两者皆空 ⇒ null（渲染器走逐字旧行为：扁平下拉栏）。
    /// </summary>
    public static FramePolicy? FromLegacy(
        AttachGrouping? grouping, AttachClassification? classification)
    {
        if (grouping is null && classification is null) return null;

        var kinds = new Dictionary<string, FrameRoleSpec>(StringComparer.Ordinal);
        var terminalKinds = new HashSet<string>(StringComparer.Ordinal);

        void Ensure(string kind, string roleId)
        {
            var badge = classification is not null
                && classification.Kinds.TryGetValue(kind, out var spec)
                    ? spec.Badge
                    : null;
            kinds[kind] = new FrameRoleSpec(kind, roleId, badge);
        }

        if (classification is not null)
        {
            foreach (var pair in classification.Kinds) Ensure(pair.Key, FrameRoleIds.Plain);
        }

        if (grouping is not null)
        {
            foreach (var kind in grouping.BoundaryKinds) Ensure(kind, FrameRoleIds.Boundary);
            foreach (var kind in grouping.TrailerKinds) Ensure(kind, FrameRoleIds.Trailer);
            foreach (var kind in grouping.CallKinds) Ensure(kind, FrameRoleIds.Call);
            foreach (var kind in grouping.ResultKinds) Ensure(kind, FrameRoleIds.Result);
            foreach (var kind in grouping.TerminalKinds)
            {
                Ensure(kind, FrameRoleIds.Trace);
                terminalKinds.Add(kind);
            }
        }

        var labels = grouping is null
            ? FrameGroupLabels.Default
            : new FrameGroupLabels(
                grouping.LeadLabel,
                grouping.IterationLabel,
                grouping.TrailerLabel,
                grouping.TraceLabel,
                string.IsNullOrWhiteSpace(grouping.TrailerLabel) ? FrameGroupLabels.Default.PostTrailer : string.Format(CultureInfo.InvariantCulture, FrameGroupLabels.Default.PostTrailerFormat, grouping.TrailerLabel),
                classification?.OtherLabel ?? FrameGroupLabels.Default.Other);

        var terminal = terminalKinds.Count == 0
            ? TerminalPolicy.None
            : new TerminalPolicy(TerminalPromotion.LastTraceInRound, terminalKinds);

        return new FramePolicy(kinds, labels, terminal);
    }
}
