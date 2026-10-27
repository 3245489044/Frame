using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Model;
using Modules.KeySpace.Rules;
using Modules.Parsing.Keys;

namespace Modules.KeySpace.Runtime;

/// <summary>
/// 坐标解析唯一实现（声明驱动、零 IO）：规则表由 <see cref="KeySpaceDeclaration"/> 构造。
/// <para>
/// 匹配：最长精确前缀优先，其次 <c>*</c>；<c>StripPrefix</c> 剥离后为空或前缀不匹配 ⇒ 按 <c>on-miss</c> 处理
/// （Fail ⇒ 不可解析，禁回退原值）。
/// </para>
/// </summary>
public sealed class CoordinateResolver : ICoordinateResolver
{
    private readonly Dictionary<string, Binding> _bindings = new(StringComparer.Ordinal);

    /// <param name="declaration">键空间声明（唯一输入；缺失规则集 ⇒ 该坐标不装配，由闸A/装载器负责报错）。</param>
    public CoordinateResolver(KeySpaceDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        foreach (var family in declaration.Families)
        {
            if (family.Axes is null || family.CoordinateRules is null) continue;

            foreach (var axis in family.Axes)
            {
                if (string.IsNullOrWhiteSpace(axis.Canonical)) continue;

                var set = family.CoordinateRules.FirstOrDefault(
                    candidate => string.Equals(candidate.Name, axis.Canonical, StringComparison.Ordinal));
                if (set is null) continue;

                var grammar = KeyGrammar.Create(
                    grammarId: family.Id + "#business",
                    template: family.BusinessKeyTemplate ?? family.Template,
                    familyPrefix: family.FamilyPrefix ?? string.Empty);

                _bindings[$"{family.Id}/{axis.Name}"] = new Binding(set, grammar);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> Coordinates => _bindings.Keys;

    /// <inheritdoc />
    public CoordinateResult Resolve(string coordinateName, string? raw)
    {
        if (string.IsNullOrWhiteSpace(coordinateName)
            || !_bindings.TryGetValue(coordinateName, out var binding))
        {
            return CoordinateResult.Unresolved("unresolved-unknown-coordinate");
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return CoordinateResult.Unresolved("unresolved-empty");
        }

        var value = raw.Trim();
        var rule = Select(binding.Set.Rules, value);
        return rule is null
            ? CoordinateResult.Unresolved("unresolved-noncanonical")
            : Apply(rule, binding, value, coordinateName);
    }

    /// <summary>规则选择：最长精确前缀 > <c>*</c>；同长取声明序首个。</summary>
    private static CoordinateRule? Select(IReadOnlyList<CoordinateRule> rules, string value)
    {
        CoordinateRule? best = null;
        CoordinateRule? wildcard = null;
        var bestLength = -1;

        foreach (var rule in rules)
        {
            if (string.Equals(rule.From, "*", StringComparison.Ordinal))
            {
                wildcard ??= rule;
                continue;
            }

            if (rule.From.Length > bestLength
                && value.Length >= rule.From.Length
                && value.StartsWith(rule.From, StringComparison.Ordinal))
            {
                best = rule;
                bestLength = rule.From.Length;
            }
        }

        return best ?? wildcard;
    }

    private static CoordinateResult Apply(CoordinateRule rule, Binding binding, string value, string coordinateName)
    {
        switch (rule.Rule)
        {
            case CoordinateRuleKind.StripPrefix:
                if (!value.StartsWith(rule.From, StringComparison.Ordinal))
                {
                    return Miss(rule, value, coordinateName);
                }

                var stripped = value[rule.From.Length..];
                return stripped.Length == 0
                    ? Miss(rule, value, coordinateName)
                    : CoordinateResult.Resolved(stripped);

            case CoordinateRuleKind.RequireFamily:
                return binding.Grammar.TryParseAny(value, out _)
                    ? CoordinateResult.Resolved(value)
                    : Miss(rule, value, coordinateName);

            default:
                return CoordinateResult.Resolved(value);
        }
    }

    private static CoordinateResult Miss(CoordinateRule rule, string value, string coordinateName)
        => rule.OnMiss == CoordinateMissPolicy.Keep
            ? CoordinateResult.Resolved(value)
            : CoordinateResult.Unresolved("unresolved-noncanonical:" + coordinateName);

    private sealed record Binding(CoordinateRuleSet Set, KeyGrammar Grammar);
}
