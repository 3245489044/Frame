using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Model;
using Modules.Parsing.Keys;

namespace Modules.KeySpace.Rules;

/// <summary>
/// 键族规则：族声明 → Modules.Parsing.Keys 文法（词法唯一，I9）。本类不实现正则/切分。
/// </summary>
public static class KeyFamilyRules
{
    public static KeyGrammar ToGrammar(KeyFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        return KeyGrammar.Create(
            grammarId: family.Id,
            template: family.Template,
            familyPrefix: family.FamilyPrefix ?? string.Empty,
            orderKeys: family.OrderKeys);
    }

    /// <summary>按族声明反解；不合模板 ⇒ null（不猜、不 StartsWith）。</summary>
    public static KeyRef? ToRef(KeyFamily family, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var grammar = ToGrammar(family);
        if (!grammar.TryParseAny(raw, out var values))
        {
            return null;
        }

        var path = new KeyPath(values.Present().Select(static pair => $"{pair.Key}={pair.Value}"));
        return new KeyRef(family.Id, raw, path);
    }

    /// <summary>声明 → 文法注册表（同 id 重复注册即抛；族前缀歧义由注册表诊断）。</summary>
    public static KeyGrammarRegistry BuildRegistry(IKeySpaceDeclaration declaration, IKeyGrammarDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var registry = new KeyGrammarRegistry(diagnostics);
        foreach (var family in declaration.Families)
        {
            registry.Register(ToGrammar(family));

            // 别名模板（如 b:{taskKey}）登记为同族别名文法：身份字段一致 ⇒ 实体判等成立。
            for (var index = 0; index < family.Aliases.Count; index++)
            {
                var alias = family.Aliases[index];
                if (string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }

                var placeholder = alias.IndexOf('{');
                var prefix = placeholder > 0 ? alias[..placeholder] : string.Empty;
                registry.Register(KeyGrammar.Create(
                    grammarId: AliasGrammarId(family.Id, index),
                    template: alias,
                    familyPrefix: prefix));
            }
        }

        return registry;
    }

    /// <summary>别名文法 id（`{family}#alias{n}`）；分类时归并回族 id。</summary>
    public static string AliasGrammarId(string familyId, int index) => $"{familyId}#alias{index}";

    /// <summary>族 id 归并（剥去别名后缀）。</summary>
    public static string FamilyOfGrammar(string grammarId)
    {
        var marker = grammarId.IndexOf("#alias", StringComparison.Ordinal);
        return marker > 0 ? grammarId[..marker] : grammarId;
    }
}
