using System;
using System.Collections.Generic;

namespace Share.Conversation;

/// <summary>
/// 会话帧**事实字段袋**（不可变值对象；会话消息/渲染项/读投影共用的唯一字段载体）。
/// <para>
/// 语义：键 = 帧载荷字段名（与 <c>read-paths.facts</c> 同一拼写）；缺失 = 键不存在（禁以空串冒充缺失）。
/// 合并规则 = 先非空者胜（同一事实多源归档时字段互补，禁「先到者整条胜出」丢字段）。
/// </para>
/// </summary>
public sealed class ConversationFacts
{
    /// <summary>空字段袋（显式无事实；与「忘记传字段」在调用点可区分）。</summary>
    public static readonly ConversationFacts Empty =
        new(new Dictionary<string, string>(StringComparer.Ordinal));

    private readonly IReadOnlyDictionary<string, string> _values;

    private ConversationFacts(IReadOnlyDictionary<string, string> values) => _values = values;

    /// <summary>字段键值（只读；序 = 来源序）。</summary>
    public IReadOnlyDictionary<string, string> Values => _values;

    /// <summary>字段数。</summary>
    public int Count => _values.Count;

    /// <summary>是否无任何字段。</summary>
    public bool IsEmpty => _values.Count == 0;

    /// <summary>由既有字段袋归一（null/空值项 ⇒ 不落袋；null 入参 ⇒ <see cref="Empty"/>）。</summary>
    public static ConversationFacts From(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0) return Empty;

        var copy = new Dictionary<string, string>(values.Count, StringComparer.Ordinal);
        foreach (var pair in values)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrEmpty(pair.Value)) continue;
            copy[pair.Key] = pair.Value;
        }

        return copy.Count == 0 ? Empty : new ConversationFacts(copy);
    }

    /// <summary>由键值对构造（空值项跳过；供窄场景/测试使用）。</summary>
    public static ConversationFacts FromPairs(params (string Key, string? Value)[] pairs)
    {
        if (pairs is null || pairs.Length == 0) return Empty;

        var copy = new Dictionary<string, string>(pairs.Length, StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrEmpty(pair.Value)) continue;
            copy[pair.Key] = pair.Value!;
        }

        return copy.Count == 0 ? Empty : new ConversationFacts(copy);
    }

    /// <summary>追加/覆盖一个字段（值空 ⇒ 原样返回）。</summary>
    public ConversationFacts With(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrEmpty(value)) return this;

        var copy = new Dictionary<string, string>(_values, StringComparer.Ordinal) { [key] = value! };
        return new ConversationFacts(copy);
    }

    /// <summary>
    /// 合并两袋字段：先非空者胜（左 = 高优先级）；右袋独有键补齐。
    /// <para>空袋 ⇒ 返回另一方（不复制）；相等 ⇒ 返回本实例（避免无意义分配）。</para>
    /// </summary>
    public ConversationFacts Merge(ConversationFacts? other)
    {
        if (other is null || other.IsEmpty) return this;
        if (IsEmpty) return other;

        Dictionary<string, string>? copy = null;
        foreach (var pair in other._values)
        {
            if (_values.ContainsKey(pair.Key)) continue;
            copy ??= new Dictionary<string, string>(_values, StringComparer.Ordinal);
            copy[pair.Key] = pair.Value;
        }

        return copy is null ? this : new ConversationFacts(copy);
    }

    /// <summary>取文本字段（不存在/空 ⇒ null）。单源 = <see cref="TextOf"/>。</summary>
    public string? Text(string key) => TextOf(_values, key);

    /// <summary>取布尔字段（<c>true/false</c> 或 <c>1/0</c>；不存在/不可解析 ⇒ null = 未知）。单源 = <see cref="FlagOf"/>。</summary>
    public bool? Flag(string key) => FlagOf(_values, key);

    /// <summary>
    /// 字段袋 → 文本（不存在/空 ⇒ null；**唯一读取口径**，禁各处再写 <c>TryGetValue</c> 副本）。
    /// </summary>
    /// <param name="values">字段袋（可空）。</param>
    /// <param name="key">字段名。</param>
    public static string? TextOf(IReadOnlyDictionary<string, string>? values, string key)
    {
        if (values is null || string.IsNullOrWhiteSpace(key)) return null;
        return values.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;
    }

    /// <summary>
    /// 字段袋 → 三态布尔（<c>true/false</c>、<c>1/0</c>；缺失/不可解析 ⇒ null = 未知，**不默认 false**）。
    /// <para>唯一实现：`FrameFacts` / 读引擎 / 事实投影共用，消除「同一字段两读一真一未知」。</para>
    /// </summary>
    /// <param name="values">字段袋（可空）。</param>
    /// <param name="key">字段名。</param>
    public static bool? FlagOf(IReadOnlyDictionary<string, string>? values, string key)
    {
        // 三态解析单源 = Modules.Node.Frame.FrameFieldBag.ParseFlag（N-02：禁第二份 "1"/"0" 解析）。
        var text = TextOf(values, key);
        return text is null ? null : Modules.Node.Frame.FrameFieldBag.ParseFlag(text);
    }
}
