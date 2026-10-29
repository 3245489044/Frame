using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod;
using Plate.DslMethod.Parsing;

namespace Usharp.Semantics;

/// <summary>
/// 编译作用域构造（I2）：<c>作用域 = 框架核心前缀 ∪ Σ 已装载语法包前缀</c>。
/// <para>
/// 未装载某包的语法 ⇒ 其前缀不进入作用域 ⇒ 解析器**不识别**该声明行，由核心闸
/// <c>UnregisteredPrefixGateRule</c>（<c>US0013</c>）显式报错（不静默丢声明）。
/// </para>
/// <para>
/// 冲突判据：两个所有者声明同一前缀（含与框架核心表同名）⇒ **启动期抛异常**，不做「后注册覆盖」。
/// 组合包（<see cref="CompositeDomainPack"/>）按其子包逐个展开参与作用域。
/// </para>
/// </summary>
public static class UsharpPrefixScope
{
    /// <summary>按已装载包构造前缀作用域（核心包前缀 + 各语法包前缀，声明序）。</summary>
    /// <exception cref="InvalidOperationException">同一前缀被两个所有者声明（含核心表冲突）。</exception>
    public static DslPrefixSet Resolve(IReadOnlyList<IUsharpDomainPack> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);

        var definitions = new List<DslPrefixDef>(DslConstants.Prefixes);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var core in DslConstants.Prefixes) owners[core.Prefix] = "core";

        foreach (var pack in Expand(packs))
        {
            if (pack is not IUsharpGrammarPack grammar) continue;

            foreach (var prefix in grammar.Prefixes)
            {
                if (owners.TryGetValue(prefix.Prefix, out var existing))
                    throw new InvalidOperationException(
                        $"Usharp 前缀冲突：'{prefix.Prefix}' 已由 '{existing}' 持有，'{pack.Id}' 重复声明（禁止后注册覆盖）");
                owners[prefix.Prefix] = pack.Id;
                definitions.Add(prefix);
            }
        }

        return new DslPrefixSet("usharp-scope", definitions);
    }

    /// <summary>展开组合包（包声明序；非组合包原样返回）。</summary>
    private static IEnumerable<IUsharpDomainPack> Expand(IEnumerable<IUsharpDomainPack> packs)
    {
        foreach (var pack in packs)
        {
            if (pack is CompositeDomainPack composite)
            {
                foreach (var inner in Expand(composite.Packs)) yield return inner;
                continue;
            }

            yield return pack;
        }
    }
}
