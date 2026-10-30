#nullable enable

using System.Collections.Generic;
using Share.Conversation.Rendering;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 结构空因（**唯一词汇表**）：空/降级是可见事实，不得压平为空串（对齐 AX.NoSilentOutlet）。
/// 文案由宿主 loc 提供；本类只提供可复算的结构码。
/// </summary>
public static class EmptyReasonCodes
{
    /// <summary>无消息（会话为空）。</summary>
    public const string NoMessages = "no-messages";

    /// <summary>结构节点为 0（域可读但无行）。</summary>
    public const string NoStructuralNodes = "no-structural-nodes";

    /// <summary>过程帧为 0。</summary>
    public const string NoProcessFrames = "no-process-frames";

    /// <summary>区域生产者未装配。</summary>
    public const string DomainNotAttached = "domain-not-attached";

    /// <summary>聚拢读面不可用（带 ReadReasons）。</summary>
    public const string GatherUnavailable = "gather-unavailable";

    /// <summary>输入被过滤后为空。</summary>
    public const string FilteredOut = "filtered-out";
}

/// <summary>
/// 渲染结果（一等出口）：段 + 空因 + 降级原因。
/// 空因/降级均可见（页面角标或文案），禁“静默换语义”。
/// </summary>
public sealed record ConversationRenderOutcome(
    IReadOnlyList<ConversationRenderSection> Sections,
    string? EmptyReason,
    string? DegradedReason);
