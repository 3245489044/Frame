using System.Globalization;
using Modules.Parsing.Keys;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 会话消息节点键的**单一权威**（对齐 <see cref="NodeTypes.ConversationMessage"/> 约定）：
/// <c>session.{name}.r{round}.msg.{seq}</c> —— <c>round</c> = 会话轮次（1 起），<c>seq</c> = **轮内**时序序号（1 起，升序 = 时序权威）。
/// <para>
/// 键含轮次段是必需项：<c>seq</c> 是轮内序号（每轮从 1 重置），若无轮次段则跨轮同名节点键相同，
/// 落库 upsert 会把上一轮消息**覆盖**掉（多轮会话历史只剩最后一轮）。轮次段使「同轮同名 → 幂等覆盖、
/// 跨轮同名 → 各自独立键」，对齐 InformationMonotonicity（历史只增不丢）。
/// </para>
/// <para>
/// 键面收敛（2026-09-26）：段序 / 分隔符 / 构造 / 反解 / 键序全部由键文法内核
/// （<c>Modules.Parsing.Keys</c> 的 <see cref="KeyGrammar"/>）提供 —— 本类只保留域语义
/// （seq 轮内重置、存量旧键容忍、时序排序口径），禁再手拼串或手写切片。
/// </para>
/// <para>兼容存量旧键 <c>session.{name}.msg.{seq}</c>（无轮次段）：反解容忍且 <see cref="ParseRound"/> 归 0，
/// 新写入一律含轮次段。</para>
/// </summary>
public static class SessionMessageKeys
{
    /// <summary>消息键文法（本域键空间单源；读侧/载体可直接经 <see cref="KeyGrammar"/> 反解，禁第二套正则）。</summary>
    public static readonly KeyGrammar KeyGrammar = KeyGrammar.Create(
        "session-message",
        "session.{container}.r{round:number}.msg.{seq:number}",
        familyPrefix: "session.",
        legacyTemplates: new[] { "session.{container}.msg.{seq:number}" },
        orderKeys: new[] { "round", "seq" });

    /// <summary>会话消息节点键前缀（= 文法族前缀；唯一来源，禁第二处字面量）。</summary>
    public static string Prefix => KeyGrammar.FamilyPrefix;

    /// <summary>轮次段标记（紧邻 <c>round</c> 占位符的字面量段，取自文法）。</summary>
    public static string RoundInfix => KeyGrammar.LiteralBefore("round");

    /// <summary>消息段标记（紧邻 <c>seq</c> 占位符的字面量段，取自文法）。</summary>
    public static string MessageInfix => KeyGrammar.LiteralBefore("seq");

    /// <summary>会话根 Id（<c>session.{name}</c>）：会话图子图端点键，与会话消息键同族。</summary>
    public static string SessionRoot(string sessionName) => Prefix + (sessionName ?? string.Empty).Trim();

    /// <summary>构造会话消息节点键：<c>session.{name}.r{round}.msg.{seq}</c>（逐字等价既有拼串，含空名退化）。</summary>
    public static string Build(string sessionName, int round, int seq)
        => KeyGrammar.BuildLenient(
            sessionName,
            round.ToString(CultureInfo.InvariantCulture),
            seq.ToString(CultureInfo.InvariantCulture));

    /// <summary>是否为会话消息节点键（族前缀 + 含消息段标记）。</summary>
    public static bool IsMessageKey(string? nodeKey)
        => !string.IsNullOrWhiteSpace(nodeKey)
           && KeyGrammar.IsFamily(nodeKey)
           && nodeKey!.Contains(MessageInfix, StringComparison.Ordinal);

    /// <summary>解析轮内时序序号；无 seq 段/不可解析 → 0（稳定序，不抛）。</summary>
    public static int ParseSeq(string? nodeKey) => ParseSegment(nodeKey, "seq");

    /// <summary>解析轮次（<c>.r{round}.msg.</c> 段）；无轮次段（存量旧键/非消息键）→ 0（不抛）。</summary>
    public static int ParseRound(string? nodeKey) => ParseSegment(nodeKey, "round");

    /// <summary>
    /// 会话消息时序比较（**读侧排序单一权威**）：先轮次、再轮内序号、最后键名字典序（稳定兜底）。
    /// 消费方（会话投影/上下文构建）一律用本比较器，禁各自 <c>OrderBy(ParseSeq)</c>（消除排序口径漂移）。
    /// </summary>
    public static int Compare(string? left, string? right) => KeyGrammar.Comparer.Compare(left, right);

    /// <summary>时序排序比较器（<see cref="Compare"/> 的 IComparer 形态；供 <c>OrderBy(k =&gt; k, ...)</c> 复用）。</summary>
    public static readonly IComparer<string> OrderComparer = KeyGrammar.Comparer;

    /// <summary>段值解析（缺段/不可解析 ⇒ 0；反解统一经文法，禁手写切片）。</summary>
    private static int ParseSegment(string? nodeKey, string name)
        => !string.IsNullOrWhiteSpace(nodeKey)
           && KeyGrammar.TryParseAny(nodeKey, out var values)
           && values.TryGetInt(name, out var value)
            ? value
            : 0;
}
