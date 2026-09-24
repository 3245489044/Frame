using System;
using System.Collections.Generic;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 会话消息节点键的**单一权威**（对齐 <see cref="NodeTypes.ConversationMessage"/> 约定）：
/// <c>session.{name}.r{round}.msg.{seq}</c> —— <c>round</c> = 会话轮次（1 起），<c>seq</c> = **轮内**时序序号（1 起，升序 = 时序权威）。
/// <para>
/// 键含轮次段是必需项：<c>seq</c> 是轮内序号（每轮从 1 重置），若无轮次段则跨轮同名节点键相同，
/// 落库 upsert 会把上一轮消息**覆盖**掉（多轮会话历史只剩最后一轮）。轮次段使「同轮同名 → 幂等覆盖、
/// 跨轮同名 → 各自独立键」，对齐 InformationMonotonicity（历史只增不丢）。
/// </para>
/// <para>键格式只此一处生成/解析，禁各处手拼（消除「键格式漂移 → 会话子图投影取不到节点」）。</para>
/// <para>兼容存量旧键 <c>session.{name}.msg.{seq}</c>（无轮次段）：解析容忍且 <see cref="ParseRound"/> 归 0，
/// 新写入一律含轮次段。</para>
/// </summary>
public static class SessionMessageKeys
{
    /// <summary>会话消息节点键前缀（<c>session.</c>）。</summary>
    public const string Prefix = "session.";

    /// <summary>轮次段标记（<c>.r</c>，后接轮次整数）。</summary>
    public const string RoundInfix = ".r";

    /// <summary>消息段标记（<c>.msg.</c>）。</summary>
    public const string MessageInfix = ".msg.";

    /// <summary>会话根 Id（<c>session.{name}</c>）：会话图子图端点键，与会话消息键同族。</summary>
    public static string SessionRoot(string sessionName) => Prefix + (sessionName ?? string.Empty).Trim();

    /// <summary>构造会话消息节点键：<c>session.{name}.r{round}.msg.{seq}</c>（name 须非空；round/seq 须 ≥ 1）。</summary>
    public static string Build(string sessionName, int round, int seq)
        => $"{Prefix}{sessionName}{RoundInfix}{round}{MessageInfix}{seq}";

    /// <summary>是否为会话消息节点键（前缀 + 含消息段标记）。</summary>
    public static bool IsMessageKey(string? nodeKey)
        => !string.IsNullOrWhiteSpace(nodeKey)
           && nodeKey!.StartsWith(Prefix, StringComparison.Ordinal)
           && nodeKey.Contains(MessageInfix, StringComparison.Ordinal);

    /// <summary>解析轮内时序序号（末尾整数段）；无 seq 段/不可解析 → 0（稳定序，不抛）。</summary>
    public static int ParseSeq(string? nodeKey)
    {
        if (!IsMessageKey(nodeKey)) return 0;
        var index = nodeKey!.LastIndexOf(MessageInfix, StringComparison.Ordinal) + MessageInfix.Length;
        return int.TryParse(nodeKey.Substring(index), out var seq) ? seq : 0;
    }

    /// <summary>解析轮次（<c>.r{round}.msg.</c> 段）；无轮次段（存量旧键/非消息键）→ 0（不抛）。</summary>
    public static int ParseRound(string? nodeKey)
    {
        if (!IsMessageKey(nodeKey)) return 0;
        var msgIndex = nodeKey!.LastIndexOf(MessageInfix, StringComparison.Ordinal);
        var head = nodeKey.Substring(0, msgIndex);
        var roundIndex = head.LastIndexOf(RoundInfix, StringComparison.Ordinal);
        if (roundIndex < 0) return 0;
        var text = head.Substring(roundIndex + RoundInfix.Length);
        return int.TryParse(text, out var round) ? round : 0;
    }

    /// <summary>
    /// 会话消息时序比较（**读侧排序单一权威**）：先轮次、再轮内序号、最后键名字典序（稳定兜底）。
    /// 消费方（会话投影/上下文构建）一律用本比较器，禁各自 <c>OrderBy(ParseSeq)</c>（消除排序口径漂移）。
    /// </summary>
    public static int Compare(string? left, string? right)
    {
        var byRound = ParseRound(left).CompareTo(ParseRound(right));
        if (byRound != 0) return byRound;
        var bySeq = ParseSeq(left).CompareTo(ParseSeq(right));
        return bySeq != 0 ? bySeq : string.CompareOrdinal(left, right);
    }

    /// <summary>时序排序比较器（<see cref="Compare"/> 的 IComparer 形态；供 <c>OrderBy(k =&gt; k, ...)</c> 复用）。</summary>
    public static readonly IComparer<string> OrderComparer = Comparer<string>.Create(Compare);
}
