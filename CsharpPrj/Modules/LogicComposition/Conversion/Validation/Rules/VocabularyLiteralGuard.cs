using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.LogicComposition.Conversion.Validation.Rules;

/// <summary>
/// 词表单源守卫（通用）：遍历规则谓词树，凡 <c>equals(路径, 字面量)</c> 且路径命中「词表落点」映射者，
/// 字面量必须已在受控注册表的词表内 —— 否则规则表抄了一份独立取值域（漂移面），构筑期即应拦下。
/// <para>
/// 判据来源 = 声明面词表（经 <see cref="IPredicateRegistry{TModel}"/> 提供）；本类只做**对账**，
/// 不产生取值域、不改词表（词表单源不破）。
/// </para>
/// </summary>
public static class VocabularyLiteralGuard
{
    /// <summary>逐条找出词表越界（返回违规描述；空表 = 全部合规）。</summary>
    /// <typeparam name="TModel">领域模型。</typeparam>
    /// <param name="specs">规则表。</param>
    /// <param name="pathToWhitelistKey">事实路径 → 词表键（仅登记项参与守卫；未登记路径不约束）。</param>
    /// <param name="registry">受控注册表（词表来源）。</param>
    public static IReadOnlyList<string> FindViolations<TModel>(
        IEnumerable<RuleSpec> specs,
        IReadOnlyDictionary<string, string> pathToWhitelistKey,
        IPredicateRegistry<TModel> registry)
    {
        var violations = new List<string>();
        if (specs is null || pathToWhitelistKey is null || registry is null) return violations;

        foreach (var spec in specs)
        {
            if (spec?.Require is null) continue;

            foreach (var node in spec.Require.SelfAndDescendants())
            {
                if (!string.Equals(node.Kind, PredicateCatalog.Equals_, StringComparison.Ordinal)) continue;
                if (node.Operands.Count < 2) continue;

                var path = node.Operands[0].Text;
                var literal = node.Operands[1].Text;
                if (path is null || literal is null) continue;
                if (!pathToWhitelistKey.TryGetValue(path, out var whitelistKey)) continue;
                if (!registry.TryGetWhitelist(whitelistKey, out var allowed)) continue;
                // netstandard2.0 无 Contains(string, StringComparison) 重载：以 Any + string.Equals 等价表达。
                if (allowed.Any(item => string.Equals(item, literal, StringComparison.Ordinal))) continue;

                violations.Add(
                    $"规则 {spec.Name}：字面量 '{literal}' 不在声明面词表 {whitelistKey}（{string.Join("/", allowed)}）");
            }
        }

        return violations;
    }
}
