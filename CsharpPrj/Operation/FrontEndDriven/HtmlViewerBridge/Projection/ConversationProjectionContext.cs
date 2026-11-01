#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Modules.Node.Frame;
using Share.Conversation;
using Share.Conversation.ContextBinding;
using Share.Conversation.Presentation;
using Share.Conversation.Rendering;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 投影上下文（**只读数据 + 单点谓词**）：模板能力面、语义表、组文案、折叠策略、轮次注记。
/// <para>纪律：投影器不得各自解释模板键/通道/文案；一切判定经本类（单一裁决点）。</para>
/// </summary>
internal sealed class ConversationProjectionContext
{
    public ConversationProjectionContext(
        TemplateSurface surface,
        FramePolicy? policy,
        FrameGroupLabels labels,
        FoldPolicy fold,
        IReadOnlyDictionary<int, string>? roundLabels,
        string emptyText)
    {
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
        Labels = labels ?? throw new ArgumentNullException(nameof(labels));
        Fold = fold ?? throw new ArgumentNullException(nameof(fold));
        Policy = policy;
        RoundLabels = roundLabels;
        EmptyText = emptyText;
    }

    /// <summary>模板能力面（唯一解析结果）。</summary>
    public TemplateSurface Surface { get; }

    /// <summary>帧语义表（null = 未注入）。</summary>
    public FramePolicy? Policy { get; }

    /// <summary>组/摘要文案与格式。</summary>
    public FrameGroupLabels Labels { get; }

    /// <summary>折叠策略（声明 + 运行态）。</summary>
    public FoldPolicy Fold { get; }

    /// <summary>轮次注记（轮 → 短标签）。</summary>
    public IReadOnlyDictionary<int, string>? RoundLabels { get; }

    /// <summary>空态文案（宿主注入）。</summary>
    public string EmptyText { get; }

    /// <summary>头提示层判定（**判据 = 段目录声明**：固定段 UiStage ∪ L0 分段 stage，非字符串前缀启发式）。</summary>
    public bool IsPrompt(ConversationRenderItem item) => PromptSegmentCatalog.IsUiStage(item.Stage);

    /// <summary>呈现声明（未注入语义表 ⇒ 空声明：全部 Unknown，可见占位）。</summary>
    public PresentationDeclaration Presentation => Policy?.Presentation ?? PresentationDeclaration.Empty;

    /// <summary>呈现身份（唯一派生点：声明 + 事实）。</summary>
    /// <param name="item">渲染项。</param>
    public (string ClassId, PresentationOrigin Origin) PresentationOf(ConversationRenderItem item)
        => PresentationResolver.Resolve(Presentation, item.Role, item.Channel, item.Presentation, item.Terminal);

    /// <summary>行模板键（声明映射；未声明 ⇒ 声明兜底模板，仍缺 ⇒ 空串，可见降级）。</summary>
    /// <param name="item">渲染项。</param>
    public string TemplateOf(ConversationRenderItem item)
        => Presentation.TemplateOf(PresentationOf(item).ClassId, Surface.DefaultTemplate);

    /// <summary>样式 token（声明映射）。</summary>
    /// <param name="item">渲染项。</param>
    public string StyleOf(ConversationRenderItem item)
        => Presentation.StyleOf(PresentationOf(item).ClassId);

    /// <summary>操作流判定（**唯一判据 = 声明面**）：Operation 面永不出气泡。</summary>
    public bool IsOperation(ConversationRenderItem item)
        => Presentation.SurfaceOf(PresentationOf(item).ClassId) == PresentationSurface.Operation;

    /// <summary>气泡锚点（单源谓词，委托 <see cref="AttachPlanner.IsBubbleAnchor(ConversationRenderItem, PresentationDeclaration)"/>）。</summary>
    public bool IsAnchor(ConversationRenderItem item) => AttachPlanner.IsBubbleAnchor(item, Presentation);

    /// <summary>
    /// 是否显示来源角标（**判据 = 声明的类位 <c>answerBadge</c>**；不再由角色+kind+面组合推断）。
    /// </summary>
    /// <param name="item">渲染项。</param>
    public bool HasAnswerBadge(ConversationRenderItem item)
        => Presentation.HasAnswerBadge(PresentationOf(item).ClassId);

    /// <summary>轮标题（0 = 会话级）。</summary>
    public string RoundTitle(int round)
        => round <= 0
            ? Labels.SessionScope
            : string.Format(CultureInfo.InvariantCulture, Labels.RoundTitleFormat, round);

    /// <summary>轮次成本注记（无上下文/无该轮 ⇒ 空串；模板判空不渲染）。</summary>
    public string CostLabel(int round)
        => RoundLabels is { } map && map.TryGetValue(round, out var label) ? label : string.Empty;

    /// <summary>降级文案（格式单源 = <see cref="FrameGroupLabels.DegradedFormat"/>）。</summary>
    public string DegradedText(string reason)
        => string.Format(CultureInfo.InvariantCulture, Labels.DegradedFormat, reason);
}

