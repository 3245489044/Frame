#nullable enable

using System;
using System.Collections.Generic;
using Modules.Node.Frame;
using Share.Conversation.Presentation;

namespace Share.Conversation.Rendering;

/// <summary>
/// 过程帧计划适配器（领域绑定；机制已下沉 <see cref="FrameSequencer{T}"/>）：
/// 把 <see cref="ConversationRenderItem"/> 投影为中性 <see cref="FrameItem"/>，
/// 锚点判定仍委托 <see cref="PresentationResolver"/>（唯一判据 = 声明面的结构面）。
/// <para>归属/分组/配对算法不再在本层实现（FR3：全仓单一实现）。</para>
/// </summary>
public static class AttachPlanner
{
    private static readonly IReadOnlyDictionary<string, FrameRoleSpec> EmptyRoles =
        new Dictionary<string, FrameRoleSpec>(StringComparer.Ordinal);

    /// <summary>
    /// 气泡锚点判定（**唯一判据 = 声明面的结构面**）：只有 <see cref="PresentationSurface.Bubble"/> 可以进入对话气泡面。
    /// </summary>
    /// <param name="item">渲染项。</param>
    /// <param name="declaration">呈现声明（产品策略）。</param>
    public static bool IsBubbleAnchor(ConversationRenderItem item, PresentationDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(declaration);
        var (classId, _) = PresentationResolver.Resolve(
            declaration, item.Role, item.Channel, item.Presentation, item.Terminal);
        return declaration.IsBubbleSurface(classId);
    }

    /// <summary>顶层分组（含前导折叠集）：事实锚点 + 前最近锚点折叠（输入恰消费一次）。</summary>
    public static FrameTopLevelPlan<ConversationRenderItem> Plan(
        IReadOnlyList<ConversationRenderItem> items, FramePolicy? policy)
        => Sequencer(policy).Plan(items);

    /// <summary>顺序分组：边界开组 / 收束只收本类 / 收束后异类开新组 / 调用与结果配对。</summary>
    public static FrameAttachPlan<ConversationRenderItem> PlanGroups(
        IReadOnlyList<ConversationRenderItem> attach, FramePolicy policy)
        => Sequencer(policy).PlanGroups(attach);

    private static FrameSequencer<ConversationRenderItem> Sequencer(FramePolicy? policy)
    {
        var declaration = policy?.Presentation ?? PresentationDeclaration.Empty;
        var roles = policy?.Kinds ?? EmptyRoles;
        var labels = policy?.Labels ?? FrameGroupLabels.Default;
        return new FrameSequencer<ConversationRenderItem>(
            roles,
            new FrameGroupText(labels.Lead, labels.Iteration, labels.Trailer, labels.PostTrailer),
            static item => new FrameItem(
                item.Stage ?? string.Empty,
                item.Round,
                item.Text ?? string.Empty,
                item.Presentation,
                null,
                item.Terminal),
            item => IsBubbleAnchor(item, declaration));
    }
}
