using System.Collections.Concurrent;

namespace Modules.Parsing.Keys;

/// <summary>
/// 模板级入口（**键模板唯一实现的门面**）：只有模板字符串、尚无已注册文法的消费面
/// （声明装载 / 位置键求值 / 身份模板拆段）经本类消费；与 <see cref="KeyGrammar"/> 共用同一
/// <see cref="KeyTemplate"/> 内核 —— 禁第二套占位符词法或反解正则。
/// </summary>
public static class KeyTemplates
{
    /// <summary>模板 → 解析产物缓存（禁每次重解析/重编译正则；纯只读缓存，非可变配置）。</summary>
    private static readonly ConcurrentDictionary<string, KeyTemplate> Cache = new(StringComparer.Ordinal);

    /// <summary>无文法归属时的诊断主体（仅用于异常文案，不参与语义）。</summary>
    private const string DeclarationSite = "(template)";

    private static readonly IReadOnlyDictionary<string, string> EmptyValues =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>占位符名（序 = 模板序）。</summary>
    public static IReadOnlyList<string> Placeholders(string template) => Parse(template).PlaceholderNames;

    /// <summary>段序列（含字面量段；序 = 模板序）—— 供身份规范/位置校验复用同一次拆段。</summary>
    public static IReadOnlyList<KeySegment> Segments(string template) => Parse(template).Segments;

    /// <summary>模板是否含指定占位符（序数占位校验用）。</summary>
    public static bool ContainsPlaceholder(string template, string name) =>
        Parse(template).PlaceholderNames.Contains(name, StringComparer.Ordinal);

    /// <summary>
    /// 正向求值（缺值 ⇒ false + <paramref name="missing"/>）。
    /// <para>
    /// **兼容契约**：失败时 <paramref name="key"/> 仍为「缺值段代入空串」的半成品键（既有 <c>KeyTemplateEvaluator</c>
    /// 语义，逐字保留以零消费方改动）；需要严格语义（失败即空键）请改用
    /// <see cref="KeyGrammar.TryBuild(IReadOnlyDictionary{string, string}, out string, out IReadOnlyList{string})"/>。
    /// </para>
    /// </summary>
    public static bool TryEvaluate(
        string template,
        IReadOnlyDictionary<string, string> values,
        out string key,
        out IReadOnlyList<string> missing)
        => Parse(template).TryEvaluate(values, renderPartial: true, out key, out missing);

    /// <summary>正向求值（缺值即抛；调用方须先经 <see cref="TryEvaluate"/> 可见化）。</summary>
    public static string Evaluate(string template, IReadOnlyDictionary<string, string> values)
        => TryEvaluate(template, values, out var key, out var missing)
            ? key
            : throw new InvalidOperationException($"键模板求值缺值: {string.Join(",", missing)}");

    /// <summary>反向反解（与该模板不同形 ⇒ false + 空值袋，不猜）。</summary>
    public static bool TryParseValues(string template, string key, out IReadOnlyDictionary<string, string> values)
    {
        if (Parse(template).TryParse(key, out var parsed))
        {
            values = parsed.ToDictionary();
            return true;
        }

        values = EmptyValues;
        return false;
    }

    /// <summary>锚定反解正则（诊断 / 机检脚本用）。</summary>
    public static string ToRegexPattern(string template) => Parse(template).Pattern.ToString();

    /// <summary>
    /// 同域可区分性（按**形状**比较：组名不同不算可区分；<c>{a}.{b}</c> 与 <c>{b}.{a}</c> 属同一键空间）。
    /// </summary>
    public static bool IsDistinguishable(string left, string right) =>
        !string.Equals(
            Parse(left).ShapePattern.ToString(),
            Parse(right).ShapePattern.ToString(),
            StringComparison.Ordinal);

    private static KeyTemplate Parse(string template) =>
        Cache.GetOrAdd(template, static text => new KeyTemplate(text, DeclarationSite));
}
