using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Model;
using Modules.KeySpace.Rules;
using Modules.Parsing.Keys;

namespace Modules.KeySpace.Dispatch;

/// <summary>
/// 声明驱动归一化唯一实现：身份文法 → 别名文法 → 业务键投影 → 业务键文法。
/// <para>
/// 只处理**声明了 <see cref="KeyFamily.BusinessKeyTemplate"/> 的族**（= 可分发的目标族）：
/// 未声明业务投影的族（如端口/节点面）不参与分发归一，避免万能模板吞掉一切字符串。
/// </para>
/// <para>纯函数 + 无 IO；实例键→业务键的领域映射经 <see cref="IBusinessKeyResolver"/> 注入。</para>
/// </summary>
public sealed class DeclarationTargetNormalizer : ITargetNormalizer
{
    private readonly IKeySpaceDeclaration _declaration;
    private readonly IBusinessKeyResolver? _resolver;
    private readonly List<KeyFamily> _families = new();
    private readonly Dictionary<string, KeyGrammar[]> _identity = new(StringComparer.Ordinal);
    private readonly Dictionary<string, KeyGrammar> _business = new(StringComparer.Ordinal);
    private readonly HashSet<string> _irreversiblePrefixes = new(StringComparer.Ordinal);

    public DeclarationTargetNormalizer(IKeySpaceDeclaration declaration, IBusinessKeyResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        _declaration = declaration;
        _resolver = resolver;

        foreach (var family in declaration.Families)
        {
            if (string.IsNullOrWhiteSpace(family.BusinessKeyTemplate))
            {
                continue;
            }

            _families.Add(family);
            _identity[family.Id] = BuildIdentityGrammars(family);
            _business[family.Id] = KeyGrammar.Create(
                grammarId: family.Id + "#business",
                template: family.BusinessKeyTemplate!,
                familyPrefix: LiteralPrefixOf(family.BusinessKeyTemplate!));

            // 不可逆派生前缀（reversible=False）：剥出的是实例哈希，不得落入业务键文法兜底。
            if (family.DerivedPrefixes is { Count: > 0 } prefixes
                && family.DerivedPrefixReversibility is { Count: > 0 } reversibility)
            {
                foreach (var prefix in prefixes)
                {
                    if (!string.IsNullOrEmpty(prefix)
                        && reversibility.TryGetValue(prefix, out var reversible)
                        && !reversible)
                    {
                        _irreversiblePrefixes.Add(prefix);
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public bool IsConfigured => _families.Count > 0;

    /// <inheritdoc />
    public NormalizedKey Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new NormalizedKey(null, raw ?? string.Empty, NormalizeOutcome.Unresolvable, "blank");
        }

        // 1) 身份/别名文法（原文即规范形态；值取模板占位值，剥离族前缀）。
        foreach (var family in _families)
        {
            var grammars = _identity[family.Id];
            for (var index = 0; index < grammars.Length; index++)
            {
                if (!grammars[index].TryParseAny(raw, out var values))
                {
                    continue;
                }

                var value = FirstValue(values);
                var path = new KeyPath(values.Present().Select(static pair => $"{pair.Key}={pair.Value}"));
                var outcome = index == 0 ? NormalizeOutcome.Resolved : NormalizeOutcome.AliasRestored;
                return new NormalizedKey(new KeyRef(family.Id, value, path), raw, outcome, null);
            }
        }

        // 2) 不可逆派生前缀（声明 reversible=False）⇒ 显式不可还原：
        //    剥前缀得到的是实例哈希（幽灵业务键），且不得落入业务键文法兜底（否则原文被当业务键放行）。
        foreach (var prefix in _irreversiblePrefixes)
        {
            if (raw.StartsWith(prefix, StringComparison.Ordinal))
            {
                return new NormalizedKey(null, raw, NormalizeOutcome.Unresolvable, "irreversible-derived-key");
            }
        }

        // 3) 业务键投影（领域适配器：实例键/派生前缀 → 业务键）。
        var restored = _resolver?.Resolve(raw, _declaration);
        if (restored is { IsEmpty: false })
        {
            return new NormalizedKey(restored, raw, NormalizeOutcome.BusinessKeyRestored, null);
        }

        // 4) 业务键文法（裸业务键，如 tp_hist_read_child）。
        foreach (var family in _families)
        {
            if (!_business.TryGetValue(family.Id, out var grammar) || !grammar.TryParseAny(raw, out var values))
            {
                continue;
            }

            var value = FirstValue(values);
            var path = new KeyPath(values.Present().Select(static pair => $"{pair.Key}={pair.Value}"));
            return new NormalizedKey(new KeyRef(family.Id, value, path), raw, NormalizeOutcome.BusinessKeyRestored, null);
        }

        return new NormalizedKey(null, raw, NormalizeOutcome.Unresolvable, "unknown-family");
    }

    private static string FirstValue(KeyValues values)
    {
        foreach (var pair in values.Present())
        {
            if (!string.IsNullOrEmpty(pair.Value))
            {
                return pair.Value;
            }
        }

        return string.Empty;
    }

    private static KeyGrammar[] BuildIdentityGrammars(KeyFamily family)
    {
        var grammars = new List<KeyGrammar>
        {
            KeyGrammar.Create(
                grammarId: family.Id,
                template: family.Template,
                familyPrefix: family.FamilyPrefix ?? string.Empty,
                orderKeys: family.OrderKeys),
        };

        for (var index = 0; index < family.Aliases.Count; index++)
        {
            var alias = family.Aliases[index];
            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            grammars.Add(KeyGrammar.Create(
                grammarId: KeyFamilyRules.AliasGrammarId(family.Id, index),
                template: alias,
                familyPrefix: LiteralPrefixOf(alias)));
        }

        return grammars.ToArray();
    }

    private static string LiteralPrefixOf(string template)
    {
        var placeholder = template.IndexOf('{');
        return placeholder > 0 ? template[..placeholder] : string.Empty;
    }
}
