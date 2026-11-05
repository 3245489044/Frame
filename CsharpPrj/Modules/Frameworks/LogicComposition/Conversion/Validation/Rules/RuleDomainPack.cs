using System;
using System.Collections.Generic;
using Share.LogicComposition.Conversion.Packs;
using Share.LogicComposition.Conversion.Validation;

namespace Share.LogicComposition.Conversion.Validation.Rules;

/// <summary>
/// 领域包（通用实现）：把「包标识 + 诊断码段 + 规则表 + 取值端口 + 受控注册表」绑定为可装配单元，
/// 规则集构筑**唯一经** <see cref="RuleSpecEngine"/>（不在域侧另写判据解释，防第二真源）。
/// <para>斜切关注点由消费方承担：规则表装载与校验（文件/格式/结论映射/词表字面量守卫）留在域侧，
/// 本类只承载「数据 → 规则执行体」的构筑与缓存（首次访问构筑，失败即抛 <see cref="RuleSpecBuildException"/>）。</para>
/// </summary>
/// <typeparam name="TModel">领域模型。</typeparam>
public sealed class RuleDomainPack<TModel> : IDomainPack<TModel>
{
    private readonly IReadOnlyList<RuleSpec> _specs;
    private readonly IRuleObjectModel<TModel> _objectModel;
    private readonly IPredicateRegistry<TModel> _registry;
    private IReadOnlyList<IGateRule<TModel>>? _rules;

    /// <summary>构筑领域包。</summary>
    /// <param name="id">包标识（如 <c>governance</c>）。</param>
    /// <param name="codePrefixes">诊断码段（唯一真源声明，如 <c>RG</c>）。</param>
    /// <param name="specs">规则表（声明序即执行序）。</param>
    /// <param name="objectModel">取值端口。</param>
    /// <param name="registry">受控注册表。</param>
    public RuleDomainPack(
        string id,
        IReadOnlyList<string> codePrefixes,
        IReadOnlyList<RuleSpec> specs,
        IRuleObjectModel<TModel> objectModel,
        IPredicateRegistry<TModel> registry)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        DiagnosticCodePrefixes = codePrefixes ?? throw new ArgumentNullException(nameof(codePrefixes));
        _specs = specs ?? throw new ArgumentNullException(nameof(specs));
        _objectModel = objectModel ?? throw new ArgumentNullException(nameof(objectModel));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> DiagnosticCodePrefixes { get; }

    /// <inheritdoc />
    public IReadOnlyList<IGateRule<TModel>> GateRules
        => _rules ??= RuleSpecEngine.Build(this, _specs, _objectModel, _registry);
}
