using System.Globalization;
using Modules.Parsing.Keys;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 会话消息节点键的**单一权威**（对齐 <see cref="NodeTypes.ConversationMessage"/> 约定，W2-A2 升级）：
/// <c>session.{name}.r{round}.{kind}.{ordinal}</c> —— <c>round</c> = 会话轮次（1 起），
/// <c>kind</c> = 内容类别（∈ 识别词表），<c>ordinal</c> = **轮内全局到达序**（1 起，升序 = 时序权威）。
/// <para>
/// 键含轮次段是必需项：<c>ordinal</c> 是轮内序号（每轮从 1 重置），若无轮次段则跨轮同名节点键相同，
/// 落库 upsert 会把上一轮消息**覆盖**掉（多轮会话历史只剩最后一轮）。轮次段使「同轮同名 → 幂等覆盖、
/// 跨轮同名 → 各自独立键」，对齐 InformationMonotonicity（历史只增不丢）。
/// </para>
/// <para>
/// 键面收敛（2026-09-26）：段序 / 分隔符 / 构造 / 反解 / 键序全部由键文法内核
/// （<c>Modules.Parsing.Keys</c> 的 <see cref="KeyGrammar"/>）提供 —— 本类只保留域语义
/// （ordinal 轮内重置、存量旧键容忍、时序排序口径），禁再手拼串或手写切片。
/// </para>
/// </summary>
public static class SessionMessageKeys
{
    /// <summary>消息键文法（本域键空间单源；读侧/载体可直接经 <see cref="KeyGrammar"/> 反解，禁第二套正则）。</summary>
    /// <remarks>
    /// W2-A2/A5：身份升级为四域统一的 <c>{container}.r{round}.{kind}.{ordinal}</c>
    /// （<c>msg{seq}</c> infix 与 <c>seq</c> 占位退场，逐字对齐声明面 <c>Config\nodes\messages.json</c>）。
    /// <para>
    /// <c>ordinal</c> = <b>轮内全局到达序</b>（非类内序）⇒ <c>orderKeys=[round,ordinal]</c> 仍为时序序，
    /// 读侧排序不因键形切换而分组漂移（对齐 <c>recognizers.json</c> 的 <c>ordinal:"arrival"</c>）。
    /// </para>
    /// <para>
    /// 存量键（<c>…msg.{seq}</c>，含最旧的无轮次形态）经 <c>legacyTemplates</c> <b>只读</b>反解；
    /// legacy 模板**沿用同一占位名 <c>ordinal</c>**（<c>KeyOrdinal.GrammarComparer</c> 按键名取值 ⇒
    /// 新旧键共用同一 <c>orderKeys</c>，排序口径单一，无第二套序）。
    /// </para>
    /// </remarks>
    public static readonly KeyGrammar KeyGrammar = KeyGrammar.Create(
        "session-message",
        "session.{container}.r{round:number}.{kind}.{ordinal:number}",
        familyPrefix: "session.",
        legacyTemplates: new[]
        {
            "session.{container}.r{round:number}.msg.{ordinal:number}",
            "session.{container}.msg.{ordinal:number}",
        },
        orderKeys: new[] { "round", "ordinal" });

    /// <summary>会话消息节点键前缀（= 文法族前缀；唯一来源，禁第二处字面量）。</summary>
    public static string Prefix => KeyGrammar.FamilyPrefix;

    /// <summary>轮次段标记（紧邻 <c>round</c> 占位符的字面量段，取自文法）。</summary>
    public static string RoundInfix => KeyGrammar.LiteralBefore("round");

    /// <summary>会话根 Id（<c>session.{name}</c>）：会话图子图端点键，与会话消息键同族。</summary>
    public static string SessionRoot(string sessionName) => Prefix + (sessionName ?? string.Empty).Trim();

    /// <summary>
    /// 构造会话消息节点键：<c>session.{name}.r{round}.{kind}.{ordinal}</c>
    /// （逐字等价声明面 <c>messages.json</c> 的位置键模板；<c>ordinal</c> = 轮内全局到达序）。
    /// </summary>
    /// <param name="sessionName">会话名（容器）。</param>
    /// <param name="round">轮次号。</param>
    /// <param name="kind">内容类别（须 ∈ 识别词表）。</param>
    /// <param name="ordinal">轮内全局到达序。</param>
    public static string Build(string sessionName, int round, string kind, long ordinal)
        => KeyGrammar.BuildLenient(
            sessionName,
            round.ToString(CultureInfo.InvariantCulture),
            kind,
            ordinal.ToString(CultureInfo.InvariantCulture));

    /// <summary>解析轮内时序序号（= 键内 <c>ordinal</c> 段；含存量 <c>msg.{seq}</c> 形态）；无段/不可解析 → 0（稳定序，不抛）。</summary>
    public static int ParseSeq(string? nodeKey) => ParseSegment(nodeKey, "ordinal");

    /// <summary>解析轮次（<c>.r{round}.</c> 段）；无轮次段（存量旧键/非消息键）→ 0（不抛）。</summary>
    public static int ParseRound(string? nodeKey) => ParseSegment(nodeKey, "round");

    /// <summary>
    /// 会话消息时序比较（**读侧排序单一权威**）：先轮次、再轮内到达序、最后键名字典序（稳定兜底）。
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
