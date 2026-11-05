using System;
using System.Collections.Generic;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Packs;

namespace Share.LogicComposition.Conversion.Validation.Rules;

/// <summary>
/// 规则表**唯一构筑器**：把 <see cref="RuleSpec"/> 列表 + 取值端口 + 受控注册表锻造成
/// <see cref="IGateRule{TModel}"/> 序列（执行序 = 声明序），执行仍复用内核 <see cref="GateEngine{TModel}"/>。
/// <para>
/// <b>构筑期硬失败</b>（对齐 RuleSpec §8）：规则形状非法 / 码段越界 / 词表键未注册 / 取值器未注册 /
/// 域谓词未注册 / 规则名重复 / 未装配端口或注册表 —— 一律抛 <see cref="RuleSpecBuildException"/>
/// 并携带**全部**错误；**不走运行期诊断流**（规则表错误 ≠ 被校验文档错误）。
/// </para>
/// <para>本类是「规则数据 → 规则执行体」的唯一转换点：不得在领域包内另写判据解释（防第二真源）。</para>
/// </summary>
public static class RuleSpecEngine
{
    /// <summary>构筑闸A 规则集（执行序 = 规则表声明序）。</summary>
    /// <typeparam name="TModel">领域模型。</typeparam>
    /// <param name="pack">领域包（提供诊断码段；不得为 null）。</param>
    /// <param name="specs">规则表。</param>
    /// <param name="objectModel">取值端口（不得为 null）。</param>
    /// <param name="registry">受控注册表（不得为 null）。</param>
    public static IReadOnlyList<IGateRule<TModel>> Build<TModel>(
        IDomainPack<TModel> pack,
        IEnumerable<RuleSpec> specs,
        IRuleObjectModel<TModel> objectModel,
        IPredicateRegistry<TModel> registry)
    {
        var errors = new List<string>();

        if (pack is null) throw new RuleSpecBuildException("领域包为空（无法校验诊断码段）");
        if (objectModel is null) throw new RuleSpecBuildException("未装配取值端口 IRuleObjectModel（无取值通道不得构筑）");
        if (registry is null) throw new RuleSpecBuildException("未装配受控注册表 IPredicateRegistry（词表/取值器无法校验）");

        var list = new List<RuleSpec>();
        if (specs is not null)
        {
            foreach (var spec in specs)
            {
                if (spec is not null) list.Add(spec);
            }
        }

        if (list.Count == 0) throw new RuleSpecBuildException("规则表为空（无判据，拒绝静默放行）");

        var names = new HashSet<string>(StringComparer.Ordinal);
        var evaluator = new PredicateEvaluator<TModel>(objectModel, registry);

        for (var i = 0; i < list.Count; i++)
        {
            var spec = list[i];

            if (!spec.TryValidate(out var reason))
            {
                errors.Add("第 " + (i + 1) + " 条：" + reason);
                continue;
            }

            if (!names.Add(spec.Name))
            {
                errors.Add("第 " + (i + 1) + " 条：规则名重复 " + spec.Name + "（Name 为归因/去重键，须唯一）");
                continue;
            }

            if (!HasCodePrefix(pack, spec.Code))
            {
                errors.Add("第 " + (i + 1) + " 条：" + spec.Name + " 的诊断码 " + spec.Code
                    + " 不在本包码段（" + string.Join("/", pack.DiagnosticCodePrefixes) + "）");
            }

            ValidateRegistryReferences(spec, registry, errors);
        }

        if (errors.Count > 0) throw new RuleSpecBuildException(errors);

        var rules = new List<IGateRule<TModel>>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            rules.Add(new RuleSpecGateRule<TModel>(list[i], objectModel, evaluator));
        }

        return rules;
    }

    /// <summary>构筑闸A 执行器（规则集 + <see cref="GateEngine{TModel}"/> 一次成型）。</summary>
    /// <typeparam name="TModel">领域模型。</typeparam>
    /// <param name="pack">领域包。</param>
    /// <param name="specs">规则表。</param>
    /// <param name="objectModel">取值端口。</param>
    /// <param name="registry">受控注册表。</param>
    public static GateEngine<TModel> BuildEngine<TModel>(
        IDomainPack<TModel> pack,
        IEnumerable<RuleSpec> specs,
        IRuleObjectModel<TModel> objectModel,
        IPredicateRegistry<TModel> registry)
        => new GateEngine<TModel>(Build(pack, specs, objectModel, registry));

    /// <summary>诊断码是否落在本包码段（Ordinal 前缀判定）。</summary>
    private static bool HasCodePrefix<TModel>(IDomainPack<TModel> pack, string code)
    {
        var prefixes = pack.DiagnosticCodePrefixes;
        if (prefixes is null || prefixes.Count == 0) return false;

        for (var i = 0; i < prefixes.Count; i++)
        {
            var prefix = prefixes[i];
            if (!string.IsNullOrEmpty(prefix) && code.StartsWith(prefix, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>
    /// 注册项校验：词表键 / 取值器 / 域谓词必须已注册（未注册 ⇒ 构筑期失败；本处只收集不抛）。
    /// </summary>
    private static void ValidateRegistryReferences<TModel>(
        RuleSpec spec, IPredicateRegistry<TModel> registry, List<string> errors)
    {
        foreach (var node in spec.Require.SelfAndDescendants())
        {
            var kind = node.Kind;

            if (string.Equals(kind, PredicateCatalog.HasEffect, StringComparison.Ordinal)
                && !registry.IsRegisteredDomainPredicate(kind))
            {
                errors.Add("规则 " + spec.Name + "：域谓词未注册 " + kind);
            }

            if (PredicateExpr.IsSetExpressionKind(kind)
                && !string.Equals(kind, PredicateCatalog.Union, StringComparison.Ordinal)
                && !registry.IsRegisteredValueProvider(kind))
            {
                errors.Add("规则 " + spec.Name + "：受控取值器未注册 " + kind);
            }

            var roles = PredicateCatalog.SlotRolesOf(kind);
            for (var i = 0; i < roles.Count && i < node.Operands.Count; i++)
            {
                if (roles[i] != PredicateRole.Whitelist) continue;

                var key = node.Operands[i].Text ?? string.Empty;
                if (!registry.TryGetWhitelist(key, out _))
                {
                    errors.Add("规则 " + spec.Name + "：" + kind + " 引用未注册词表键 " + key);
                }
            }
        }
    }
}
