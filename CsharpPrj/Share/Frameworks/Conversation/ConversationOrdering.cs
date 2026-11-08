using System;
using System.Collections.Generic;
using System.Linq;

using Share.Conversation.ContextBinding;

namespace Share.Conversation;

/// <summary>
/// 会话消息「去重 + 轮次/角色序」唯一实现（跨层复用：读投影 / 读执行器 / 补位合并）。
/// <para>去重键 = <c>(Round, Role, Stage, SubAgent, Hash(Text))</c>；<c>includeBinding=true</c> 时并入锚点键
/// （同一文本不同锚点视为不同事实）。源序即优先级：先出现者胜。</para>
/// <para>放置依据：键语义只依赖 <see cref="ConversationMessage"/> 自身，属平台层能力，消费方禁各自重写。</para>
/// </summary>
public static class ConversationOrdering
{
    /// <summary>轮内角色优先级（User &lt; Tool &lt; Assistant &lt; 其它）。</summary>
    public static int RolePriority(ConversationRole role) => role switch
    {
        ConversationRole.User => 0,
        ConversationRole.Tool => 1,
        ConversationRole.Assistant => 2,
        _ => 3,
    };

    /// <summary>稳定序：Round 升序 → 轮内角色序（同序保持源序）。</summary>
    public static IReadOnlyList<ConversationMessage> Order(IEnumerable<ConversationMessage> messages)
        => (messages ?? Enumerable.Empty<ConversationMessage>())
            .OrderBy(static m => m.Round)
            .ThenBy(static m => RolePriority(m.Role))
            .ToList();

    /// <summary>
    /// 按源序去重（先出现者胜）；**重复项合并事实字段袋**（先非空者胜），
    /// 防「同一事实多源到达时先到者字段少 ⇒ 后到者的事实被整条丢弃」。
    /// </summary>
    public static List<ConversationMessage> Distinct(
        IEnumerable<ConversationMessage> messages, bool includeBinding = false)
    {
        var result = new List<ConversationMessage>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var message in messages ?? Enumerable.Empty<ConversationMessage>())
        {
            if (message is null) continue;
            var key = Key(message, includeBinding);
            if (index.TryGetValue(key, out var existing))
            {
                result[existing] = MergeFacts(result[existing], message);
                continue;
            }

            index[key] = result.Count;
            result.Add(message);
        }

        return result;
    }

    /// <summary>同键消息的事实合并（左 = 高优先级；字段袋并集，事实标量取先非空者）。</summary>
    private static ConversationMessage MergeFacts(ConversationMessage winner, ConversationMessage other)
    {
        var winnerFacts = ConversationFacts.From(winner.Fields);
        var mergedFacts = winnerFacts.Merge(ConversationFacts.From(other.Fields));
        if (mergedFacts.Count == winnerFacts.Count
            && winner.Terminal is not null
            && winner.Archived is not null
            && winner.Presentation is not null
            && winner.Kind is not null)
        {
            return winner;
        }

        return winner with
        {
            Terminal = winner.Terminal ?? other.Terminal,
            Archived = winner.Archived ?? other.Archived,
            Presentation = winner.Presentation ?? other.Presentation,
            Kind = winner.Kind ?? other.Kind ?? mergedFacts.Text("kind"),
            Fields = mergedFacts.Values,
        };
    }

    /// <summary>去重 + 稳定排序（合并语义唯一实现）。</summary>
    public static IReadOnlyList<ConversationMessage> Merge(
        IEnumerable<ConversationMessage> messages, bool includeBinding = false)
        => Order(Distinct(messages, includeBinding));

    /// <summary>去重键（唯一键口径；禁各消费方自拼键串）。</summary>
    public static string Key(ConversationMessage message, bool includeBinding = false)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));
        var key = $"{message.Round}|{message.Role}|{KindOf(message)}|{message.SubAgent}|{PromptSnapshotBuilder.Hash(message.Text)}";
        return includeBinding ? key + "|" + message.Binding?.Key.Value : key;
    }

    /// <summary>
    /// 生效帧类别（唯一口径）：显式 <see cref="ConversationMessage.Kind"/> → 字段袋 <c>kind</c> → 旧 Stage。
    /// 去重/分组/呈现一律经本方法，禁直接以 Stage 当类别。
    /// </summary>
    public static string KindOf(ConversationMessage message)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));
        if (!string.IsNullOrWhiteSpace(message.Kind)) return message.Kind!;
        if (message.Fields is not null
            && message.Fields.TryGetValue("kind", out var kind)
            && !string.IsNullOrWhiteSpace(kind))
        {
            return kind;
        }

        return message.Stage ?? string.Empty;
    }
}
