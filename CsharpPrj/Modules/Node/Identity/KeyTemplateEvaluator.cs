using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Modules.Node.Placement;

namespace Modules.Node.Identity;

/// <summary>
/// 键模板求值（唯一实现）：正向求值（值 → 键）与反向解析（键 → 值）。
/// 位置键 = 键模板(身份键)（N2：位置不改变身份），故读写两侧共用本求值器；
/// 占位符词法亦为本类单源（<see cref="IdentitySpec"/> 复用同一原子）。
/// </summary>
public static class KeyTemplateEvaluator
{
    /// <summary>占位符词法单源：<c>{name}</c>（name 为标识符）。</summary>
    internal static readonly Regex PlaceholderRegex = new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    /// <summary>反解正则缓存（键 → 值逐键调用，禁每次重编译）。</summary>
    private static readonly ConcurrentDictionary<string, Regex> PatternCache = new(StringComparer.Ordinal);

    /// <summary>单次匹配上限：异常键不得拖死宿主（超时 ⇒ 视为不可反解，不静默当命中）。</summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    public static IReadOnlyList<string> Placeholders(string template) =>
        PlaceholderRegex.Matches(template).Select(m => m.Groups[1].Value).ToArray();

    public static bool TryEvaluate(
        string template,
        IReadOnlyDictionary<string, string> values,
        out string key,
        out IReadOnlyList<string> missing)
    {
        var absent = new List<string>();
        var rendered = PlaceholderRegex.Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (values.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }

            absent.Add(name);
            return string.Empty;
        });

        missing = absent;
        key = rendered;
        return absent.Count == 0;
    }

    public static string Evaluate(string template, IReadOnlyDictionary<string, string> values) =>
        TryEvaluate(template, values, out var key, out var missing)
            ? key
            : throw new InvalidOperationException($"键模板求值缺值: {string.Join(",", missing)}");

    /// <summary>把模板转成可反解的锚定正则（字面量转义，占位符非贪婪捕获）。</summary>
    public static string ToRegexPattern(string template) => BuildPattern(template, named: true);

    /// <summary>模板"形状"正则（占位符一律匿名）：用于同域可区分性判定（组名不参与判定）。</summary>
    internal static string ToShapePattern(string template) => BuildPattern(template, named: false);

    private static string BuildPattern(string template, bool named)
    {
        var builder = new StringBuilder("^");
        var index = 0;

        foreach (Match match in PlaceholderRegex.Matches(template))
        {
            builder.Append(Regex.Escape(template[index..match.Index]));
            builder.Append(named ? $"(?<{match.Groups[1].Value}>.+?)" : "(.+?)");
            index = match.Index + match.Length;
        }

        builder.Append(Regex.Escape(template[index..])).Append('$');
        return builder.ToString();
    }

    public static bool TryParseValues(string template, string key, out IReadOnlyDictionary<string, string> values)
    {
        var regex = PatternCache.GetOrAdd(
            ToRegexPattern(template),
            pattern => new Regex(pattern, RegexOptions.Singleline, MatchTimeout));

        Match match;
        try
        {
            match = regex.Match(key);
        }
        catch (RegexMatchTimeoutException)
        {
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            return false;
        }

        if (!match.Success)
        {
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            return false;
        }

        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in regex.GetGroupNames())
        {
            if (int.TryParse(name, out _))
            {
                continue;
            }

            parsed[name] = match.Groups[name].Value;
        }

        values = parsed;
        return true;
    }

    /// <summary>L-4：模板是否含声明的序数占位（缺失即序数无法单源分配）。</summary>
    public static bool ContainsSequencePlaceholder(string template, IReadOnlyCollection<string> sequenceKeys) =>
        sequenceKeys.Any(key => template.Contains("{" + key + "}", StringComparison.Ordinal));

    /// <summary>
    /// 同一域内键模板可区分性判定（装配期校验用）：按<b>形状</b>（占位符匿名化）比较——
    /// <c>{a}</c> 与 <c>{b}</c>、<c>{a}.{b}</c> 与 <c>{b}.{a}</c> 键空间等价，必须判为不可区分（原实现按
    /// 命名正则文本比较，组名不同即误判为"可区分"，该门近乎恒真）。
    /// </summary>
    public static bool TemplatesDistinguishable(string left, string right) =>
        !string.Equals(ToShapePattern(left), ToShapePattern(right), StringComparison.Ordinal);

    /// <summary>
    /// 位置键求值（**域侧唯一入口**，F2）：位置声明 → 键模板 → 键。
    /// <para>
    /// 存在理由：域侧构造键时不得自持键模板常量（声明面单源，N2：位置键 = 键模板(身份键)）。
    /// 未装配（位置注册表缺失 / 位置未声明 / 取值不全）⇒ <c>false</c> + reason，由调用方**可见化**；
    /// 禁回退常量（N9 / D4①：未装配不兜底）。
    /// </para>
    /// </summary>
    /// <param name="placements">位置注册表（声明装载产物；<c>null</c> = 未装配）。</param>
    /// <param name="placementId">位置 id（声明面）。</param>
    /// <param name="values">身份占位符 → 值。</param>
    /// <param name="key">位置键（成功时给出）。</param>
    /// <param name="reason">失败原因（未装配 / 未声明 / 缺值；逐字可诊断）。</param>
    public static bool TryEvaluatePlacement(
        PlacementRegistry? placements,
        string placementId,
        IReadOnlyDictionary<string, string> values,
        out string key,
        out string reason)
    {
        key = string.Empty;

        if (placements is null)
        {
            reason = "placement.unbound（位置注册表未装配：键构造点不得回退常量）";
            return false;
        }

        if (string.IsNullOrWhiteSpace(placementId))
        {
            reason = "placement.id-empty";
            return false;
        }

        if (!placements.TryGet(placementId, out var spec))
        {
            reason = $"placement.undeclared（位置 '{placementId}' 未声明；位置清单单源 = 域声明）";
            return false;
        }

        if (!TryEvaluate(spec.KeyTemplate, values, out key, out var missing))
        {
            reason = $"placement.value-missing（模板 '{spec.KeyTemplate}' 缺值: {string.Join(",", missing)}）";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>位置键求值（框架入口；语义同 <see cref="TryEvaluatePlacement(PlacementRegistry?, string, IReadOnlyDictionary{string, string}, out string, out string)"/>）。</summary>
    public static bool TryEvaluatePlacement(
        NodeFramework? framework,
        string placementId,
        IReadOnlyDictionary<string, string> values,
        out string key,
        out string reason)
        => TryEvaluatePlacement(framework?.Placements, placementId, values, out key, out reason);

    /// <summary>
    /// 位置键反解（**域侧唯一入口**（F2 反向））：位置键 → 身份占位符值。
    /// 未装配 / 位置未声明 / 键与该位置模板不同形 ⇒ <c>false</c> + reason（调用方可见化，禁回退常量）。
    /// </summary>
    /// <param name="placements">位置注册表（声明装载产物；<c>null</c> = 未装配）。</param>
    /// <param name="placementId">位置 id（声明面）。</param>
    /// <param name="key">位置键。</param>
    /// <param name="values">反解值（失败时空字典）。</param>
    /// <param name="reason">失败原因。</param>
    public static bool TryParsePlacementValues(
        PlacementRegistry? placements,
        string placementId,
        string key,
        out IReadOnlyDictionary<string, string> values,
        out string reason)
    {
        values = EmptyValues;

        if (placements is null)
        {
            reason = "placement.unbound（位置注册表未装配：键反解点不得回退常量）";
            return false;
        }

        if (string.IsNullOrWhiteSpace(placementId))
        {
            reason = "placement.id-empty";
            return false;
        }

        if (!placements.TryGet(placementId, out var spec))
        {
            reason = $"placement.undeclared（位置 '{placementId}' 未声明；位置清单单源 = 域声明）";
            return false;
        }

        if (!TryParseValues(spec.KeyTemplate, key, out var parsed))
        {
            values = EmptyValues;
            reason = $"placement.key-unparsable（键 '{key}' 与位置 '{placementId}' 的模板 '{spec.KeyTemplate}' 不同形）";
            return false;
        }

        values = parsed;
        reason = string.Empty;
        return true;
    }

    /// <summary>位置键反解（框架入口；语义同 <see cref="TryParsePlacementValues(PlacementRegistry?, string, string, out IReadOnlyDictionary{string, string}, out string)"/>）。</summary>
    public static bool TryParsePlacementValues(
        NodeFramework? framework,
        string placementId,
        string key,
        out IReadOnlyDictionary<string, string> values,
        out string reason)
        => TryParsePlacementValues(framework?.Placements, placementId, key, out values, out reason);

    /// <summary>反解失败/未装配时的空值集（调用方无需判 null）。</summary>
    private static readonly IReadOnlyDictionary<string, string> EmptyValues =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
