using System.Text;
using System.Text.RegularExpressions;

namespace Modules.Node.Identity;

/// <summary>
/// 键模板求值（唯一实现）：正向求值（值 → 键）与反向解析（键 → 值）。
/// 位置键 = 键模板(身份键)（N2：位置不改变身份），故读写两侧共用本求值器。
/// </summary>
public static class KeyTemplateEvaluator
{
    private static readonly Regex PlaceholderRegex = new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

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
    public static string ToRegexPattern(string template)
    {
        var builder = new StringBuilder("^");
        var index = 0;

        foreach (Match match in PlaceholderRegex.Matches(template))
        {
            builder.Append(Regex.Escape(template[index..match.Index]));
            builder.Append("(?<").Append(match.Groups[1].Value).Append(">.+?)");
            index = match.Index + match.Length;
        }

        builder.Append(Regex.Escape(template[index..])).Append('$');
        return builder.ToString();
    }

    public static bool TryParseValues(string template, string key, out IReadOnlyDictionary<string, string> values)
    {
        var regex = new Regex(ToRegexPattern(template), RegexOptions.Singleline);
        var match = regex.Match(key);
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

    /// <summary>同一域内键模板可区分性判定（装配期校验用）。</summary>
    public static bool TemplatesDistinguishable(string left, string right) =>
        !string.Equals(ToRegexPattern(left), ToRegexPattern(right), StringComparison.Ordinal);
}
