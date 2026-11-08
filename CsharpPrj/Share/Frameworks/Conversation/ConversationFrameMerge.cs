using System;
using System.Collections.Generic;
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

    /// <summary>去重（先到者胜；重复项合并事实袋，不丢失后到源独有字段）。</summary>
    public static IReadOnlyList<ConversationFrame> Distinct(IEnumerable<ConversationFrame>? frames)
    {
        if (frames is null) return Array.Empty<ConversationFrame>();

        var result = new List<ConversationFrame>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var frame in frames)
        {
            if (frame is null) continue;
            var key = Key(frame);
            if (index.TryGetValue(key, out var existing))
            {
                result[existing] = Merge(result[existing], frame);
                continue;
            }

            index[key] = result.Count;
            result.Add(frame);
        }

        return result;
    }

    /// <summary>稳定序：Round 升序 → 轮内角色序（同序保持源序）。</summary>
    public static IReadOnlyList<ConversationFrame> Order(IEnumerable<ConversationFrame>? frames)
    {
        if (frames is null) return Array.Empty<ConversationFrame>();

        var ordered = new List<ConversationFrame>(frames);
        ordered.Sort(static (left, right) =>
        {
            var round = left.Round.CompareTo(right.Round);
            if (round != 0) return round;
            var role = RolePriority(left.Role).CompareTo(RolePriority(right.Role));
            if (role != 0) return role;
            return left.Seq.CompareTo(right.Seq);
        });
        return ordered;
    }

    /// <summary>去重 + 稳定排序（读面合并唯一入口）。</summary>
    public static IReadOnlyList<ConversationFrame> Merge(IEnumerable<ConversationFrame>? frames)
        => Order(Distinct(frames));

    /// <summary>轮内角色优先级（单源委派 <see cref="ConversationOrdering.RolePriority"/>；禁平行复制）。</summary>
    public static int RolePriority(ConversationRole role) => ConversationOrdering.RolePriority(role);
}
