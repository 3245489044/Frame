using System;
using System.Collections.Generic;
using Modules.Foundations.Collections.Keyed;
using Share.Conversation.ContextBinding;

namespace Share.Conversation;

/// <summary>
/// 会话帧**唯一合并规则**（去重 + 事实并集 + 顺序）：
/// 同一事实多源到达时，文本/角色按源优先级，**字段袋取并集**（先非空者胜）。
/// <para>
/// 去重键：有绑定锚点 ⇒ 锚点键（同一锚点即同一事实）；否则
/// <c>(Round, Role, Kind, SubAgent, hash(Text))</c>。禁各读源再自写一套键。
/// </para>
/// </summary>
public static class ConversationFrameMerge
{
    /// <summary>去重键（唯一口径）。</summary>
    public static string Key(ConversationFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Binding is { } binding && !string.IsNullOrEmpty(binding.Key.Value))
        {
            return "anchor:" + binding.Key.Value;
        }

        return string.Join(
            '|',
            frame.Round,
            frame.Role,
            frame.Kind,
            frame.SubAgent ?? string.Empty,
            PromptSnapshotBuilder.Hash(frame.Text));
    }

    /// <summary>
    /// 合并同一事实的两帧：左（高优先级）非空字段胜出，右补齐缺口；字段袋并集。
    /// </summary>
    public static ConversationFrame Merge(ConversationFrame winner, ConversationFrame other)
    {
        ArgumentNullException.ThrowIfNull(winner);
        ArgumentNullException.ThrowIfNull(other);

        return winner with
        {
            Text = string.IsNullOrEmpty(winner.Text) ? other.Text : winner.Text,
            Stage = winner.Stage ?? other.Stage,
            SubAgent = winner.SubAgent ?? other.SubAgent,
            Channel = winner.Channel ?? other.Channel,
            Targets = winner.Targets ?? other.Targets,
            Terminal = winner.Terminal ?? other.Terminal,
            Archived = winner.Archived ?? other.Archived,
            Presentation = winner.Presentation ?? other.Presentation,
            Binding = winner.Binding ?? other.Binding,
            Origin = winner.Origin ?? other.Origin,
            AtUtc = winner.AtUtc == default ? other.AtUtc : winner.AtUtc,
            Facts = winner.Facts.Merge(other.Facts),
        };
    }

    /// <summary>去重（先到者胜；重复项合并事实袋，不丢失后到源独有字段）。单源 = <see cref="KeyedMerge.DistinctMerge{T}"/>。</summary>
    public static IReadOnlyList<ConversationFrame> Distinct(IEnumerable<ConversationFrame>? frames)
        => KeyedMerge.DistinctMerge(frames, Key, Merge);

    /// <summary>稳定序：Round 升序 → 轮内角色序（同序保持源序）。单源 = <see cref="KeyedMerge.StableOrder{T}"/>。</summary>
    public static IReadOnlyList<ConversationFrame> Order(IEnumerable<ConversationFrame>? frames)
        => KeyedMerge.StableOrder(
            frames,
            static (left, right) =>
            {
                var round = left.Round.CompareTo(right.Round);
                if (round != 0) return round;
                var role = ConversationOrdering.RolePriority(left.Role).CompareTo(ConversationOrdering.RolePriority(right.Role));
                return role != 0 ? role : left.Seq.CompareTo(right.Seq);
            });

    /// <summary>去重 + 稳定排序（读面合并唯一入口）。</summary>
    public static IReadOnlyList<ConversationFrame> Merge(IEnumerable<ConversationFrame>? frames)
        => Order(Distinct(frames));

    /// <summary>轮内角色优先级（单源委派 <see cref="ConversationOrdering.RolePriority"/>；禁平行复制）。</summary>
    [System.Obsolete("N-05：纯转发，请直接调用 ConversationOrdering.RolePriority。")]
    public static int RolePriority(ConversationRole role) => ConversationOrdering.RolePriority(role);
}
