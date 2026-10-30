#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 过程帧语义角色（**唯一语义轴**）：分组、终答提升、徽标与配对全部由本轴派生，
/// 禁止再以多组 kind 集合（BoundaryKinds/TrailerKinds/...）平行表达同一语义。
/// </summary>
public enum FrameRole
{
    /// <summary>普通帧（逐条成行）。</summary>
    Plain = 0,

    /// <summary>文本锚点（归档消息，顶部气泡）。</summary>
    Text = 1,

    /// <summary>过程文本（镜像，不冒充终答；可参与“本轮最后一段文本”的过渡提升）。</summary>
    TraceText = 2,

    /// <summary>终答实体（权威源唯一；只有该角色可升为终答气泡）。</summary>
    Answer = 3,

    /// <summary>边界帧（开新顺序组，如 llm-iteration）。</summary>
    Boundary = 4,

    /// <summary>收束帧（结算组；只接收本角色，不吞后续异类帧）。</summary>
    Trailer = 5,

    /// <summary>调用帧（可被结果帧配对嵌套）。</summary>
    Call = 6,

    /// <summary>结果帧（嵌套到最近调用行；无配对则独立成行，零丢失）。</summary>
    Result = 7,
}

/// <summary>kind → 角色 + 徽标（声明驱动的单表条目）。</summary>
public sealed record FrameKindSpec(string Kind, FrameRole Role, string? Badge);

/// <summary>终答提升策略（过渡期开关；终答实体落地后切 <see cref="EntityOnly"/>）。</summary>
public enum AnswerPromotion
{
    /// <summary>不提升（全部按过程文本呈现）。</summary>
    None = 0,

    /// <summary>过渡：本轮最后一条过程文本提升为终答气泡（带“未归档”角标）。</summary>
    LastTextInRound = 1,

    /// <summary>只认终答实体（Answer 角色）；禁以最后一条过程文本冒充终答。</summary>
    EntityOnly = 2,
}

/// <summary>终答身份策略：提升模式 + 终答 kind 集 + 文本归一化（去重单源）。</summary>
public sealed record AnswerPolicy(AnswerPromotion Promotion, IReadOnlySet<string> AnswerKinds)
{
    /// <summary>不提升/不识别终答 kind。</summary>
    public static readonly AnswerPolicy None =
        new(AnswerPromotion.None, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>去重比较用归一化：trim + 空白折叠（E9：近似重复不得生成两个气泡）。</summary>
    public string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}

/// <summary>顺序分组文案（宿主经 loc 声明注入；未注入取通用默认）。</summary>
public sealed record FrameGroupLabels(
    string Lead,
    string Iteration,
    string Trailer,
    string AnswerTrace,
    string PostTrailer,
    string Other)
{
    /// <summary>通用默认文案（非领域词表；领域标签由宿主覆盖）。</summary>
    public static readonly FrameGroupLabels Default =
        new("准备", "LLM 回合 {0}", "结算", "未归档", "结算后", "其他");
}

/// <summary>
/// 过程帧语义策略（**单一注入面**）：分类（kind→角色/徽标）+ 分组文案 + 终答身份。
/// 渲染器只读本表，不再读 <see cref="AttachClassification"/> 的组序或 <see cref="AttachGrouping"/> 的多集合。
/// </summary>
public sealed record ConversationFramePolicy(
    IReadOnlyDictionary<string, FrameKindSpec> Kinds,
    FrameGroupLabels Labels,
    AnswerPolicy Answers)
{
    /// <summary>未登记 kind ⇒ 普通帧（可见、不丢弃）。</summary>
    public FrameRole RoleOf(string? kind)
        => kind is not null && Kinds.TryGetValue(kind, out var spec) ? spec.Role : FrameRole.Plain;

    /// <summary>未登记徽标 ⇒ null（模板回退显示 kind）。</summary>
    public string? BadgeOf(string? kind)
        => kind is not null && Kinds.TryGetValue(kind, out var spec) ? spec.Badge : null;

    /// <summary>是否过程文本（可参与过渡提升）。</summary>
    public bool IsTraceText(string? kind) => RoleOf(kind) == FrameRole.TraceText;

    /// <summary>
    /// 旧选项适配（单批次兼容）：把 <see cref="AttachGrouping"/> + <see cref="AttachClassification"/>
    /// 转成单表策略；两者皆空 ⇒ null（渲染器走逐字旧行为：扁平下拉栏）。
    /// </summary>
    public static ConversationFramePolicy? FromLegacy(
        AttachGrouping? grouping, AttachClassification? classification)
    {
        if (grouping is null && classification is null) return null;

        var kinds = new Dictionary<string, FrameKindSpec>(StringComparer.Ordinal);
        var answerKinds = new HashSet<string>(StringComparer.Ordinal);

        void Ensure(string kind, FrameRole role)
        {
            var badge = classification is not null
                && classification.Kinds.TryGetValue(kind, out var spec)
                    ? spec.Badge
                    : null;
            kinds[kind] = new FrameKindSpec(kind, role, badge);
        }

        if (classification is not null)
        {
            foreach (var pair in classification.Kinds) Ensure(pair.Key, FrameRole.Plain);
        }

        if (grouping is not null)
        {
            foreach (var kind in grouping.BoundaryKinds) Ensure(kind, FrameRole.Boundary);
            foreach (var kind in grouping.TrailerKinds) Ensure(kind, FrameRole.Trailer);
            foreach (var kind in grouping.CallKinds) Ensure(kind, FrameRole.Call);
            foreach (var kind in grouping.ResultKinds) Ensure(kind, FrameRole.Result);
            foreach (var kind in grouping.AnswerKinds)
            {
                Ensure(kind, FrameRole.TraceText);
                answerKinds.Add(kind);
            }
        }

        var labels = grouping is null
            ? FrameGroupLabels.Default
            : new FrameGroupLabels(
                grouping.LeadLabel,
                grouping.IterationLabel,
                grouping.TrailerLabel,
                grouping.AnswerTraceLabel,
                string.IsNullOrWhiteSpace(grouping.TrailerLabel) ? "结算后" : grouping.TrailerLabel + "后",
                classification?.OtherLabel ?? FrameGroupLabels.Default.Other);

        var answers = answerKinds.Count == 0
            ? AnswerPolicy.None
            : new AnswerPolicy(AnswerPromotion.LastTextInRound, answerKinds);

        return new ConversationFramePolicy(kinds, labels, answers);
    }
}
