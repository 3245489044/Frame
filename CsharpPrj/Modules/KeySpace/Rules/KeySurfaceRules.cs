using System;
using System.Collections.Generic;
using System.Linq;
using Modules.KeySpace.Model;
using Modules.Parsing.Keys;

namespace Modules.KeySpace.Rules;

/// <summary>
/// 面声明 → 文法（词法唯一来源 = Modules.Parsing.Keys）：本类不实现正则与切分。
/// </summary>
public static class KeySurfaceRules
{
    /// <summary>族的面声明；未显式声明 ⇒ 以族模板作为唯一存储面（兼容既有声明）。</summary>
    public static IReadOnlyDictionary<KeySurface, KeySurfaceSpec> EffectiveSurfaces(KeyFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (family.Surfaces is { Count: > 0 })
        {
            return family.Surfaces;
        }

        return new Dictionary<KeySurface, KeySurfaceSpec>
        {
            [KeySurface.Storage] = new KeySurfaceSpec(
                KeySurface.Storage,
                new[] { family.Template },
                family.FamilyPrefix),
        };
    }

    /// <summary>取指定面的文法（templateIndex = 0 规范模板；其余为只读兼容/变体模板）。</summary>
    public static KeyGrammar Grammar(KeyFamily family, KeySurface surface, int templateIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(family);
        var surfaces = EffectiveSurfaces(family);
        if (!surfaces.TryGetValue(surface, out var spec))
        {
            throw new KeyGrammarDefinitionException(
                $"族 '{family.Id}' 未声明键面 '{surface}'", family.Id);
        }

        var templates = spec.Templates;
        if (templateIndex < 0 || templateIndex >= templates.Count)
        {
            throw new KeyGrammarDefinitionException(
                $"族 '{family.Id}' 面 '{surface}' 无模板索引 {templateIndex}", family.Id);
        }

        var prefix = spec.FamilyPrefix ?? family.FamilyPrefix ?? string.Empty;
        return KeyGrammar.Create(
            $"{family.Id}#{surface}#{templateIndex}",
            templates[templateIndex],
            prefix);
    }

    /// <summary>取规范文法（构造用）。</summary>
    public static KeyGrammar Canonical(KeyFamily family, KeySurface surface)
        => Grammar(family, surface, 0);

    /// <summary>全部可读形态（规范 + 兼容）；返回顺序 = 判定优先序。</summary>
    public static IReadOnlyList<(KeyGrammar Grammar, bool Compatible)> Readable(
        KeyFamily family, KeySurface surface)
    {
        ArgumentNullException.ThrowIfNull(family);
        var surfaces = EffectiveSurfaces(family);
        if (!surfaces.TryGetValue(surface, out var spec))
        {
            return Array.Empty<(KeyGrammar, bool)>();
        }

        var list = new List<(KeyGrammar, bool)>();
        for (var index = 0; index < spec.Templates.Count; index++)
        {
            list.Add((Grammar(family, surface, index), index > 0));
        }

        var compatible = spec.CompatibleTemplates;
        for (var index = 0; index < compatible.Count; index++)
        {
            var prefix = spec.FamilyPrefix ?? family.FamilyPrefix ?? string.Empty;
            list.Add((KeyGrammar.Create(
                $"{family.Id}#{surface}#compatible{index}", compatible[index], prefix), true));
        }

        return list;
    }
}
