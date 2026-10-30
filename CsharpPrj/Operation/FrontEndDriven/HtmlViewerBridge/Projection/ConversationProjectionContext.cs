#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Share.Conversation;
using Share.Conversation.Rendering;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 投影上下文（**只读数据 + 单点谓词**）：模板能力面、语义表、组文案、折叠策略、轮次注记。
/// <para>纪律：投影器不得各自解释模板键/通道/文案；一切判定经本类（单一裁决点）。</para>
/// </summary>
internal sealed class ConversationProjectionContext
{
    private readonly string? _promptStagePrefix;

    public ConversationProjectionContext(
        TemplateSurface surface,
        FramePolicy? policy,
        FrameGroupLabels labels,
        FoldPolicy fold,
        string? promptStagePrefix,
        IReadOnlyDictionary<int, string>? roundLabels,
        string emptyText)
    {
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
        Labels = labels ?? throw new ArgumentNullException(nameof(labels));
        Fold = fold ?? throw new ArgumentNullException(nameof(fold));
        Policy = policy;
        _promptStagePrefix = promptStagePrefix;
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

    /// <summary>头提示层（prompt:*）判定；前缀由宿主注入。</summary>
    public bool IsPrompt(ConversationRenderItem item)
        => _promptStagePrefix is not null
           && item.Stage is not null
           && item.Stage.StartsWith(_promptStagePrefix, StringComparison.Ordinal);

    /// <summary>
    /// 操作流判定（**角色派生**，不再只看写侧通道）：声明角色 = operation ⇒ 操作面板；
    /// 兼容旧数据（写侧 <see cref="MessageChannel.Operation"/>）仍成立。
    /// </summary>
    public bool IsOperation(ConversationRenderItem item)
        => item.Channel == MessageChannel.Operation
           || (Policy is not null && Policy.RoleOf(item.Stage) == FrameRoleIds.Operation);

    /// <summary>气泡锚点（单源谓词，委托 <see cref="AttachPlanner.IsBubbleAnchor"/>）。</summary>
    public bool IsAnchor(ConversationRenderItem item) => AttachPlanner.IsBubbleAnchor(item, Policy);

    /// <summary>是否「由过程帧过渡提升的终答」（来源角标可见；归档 assistant 不标）。</summary>
    public bool IsTraceAnswer(ConversationRenderItem item)
        => Policy is not null
           && item.Role == ConversationRole.Assistant
           && Policy.IsTrace(item.Stage);

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
