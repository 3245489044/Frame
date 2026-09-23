using System;
using System.Collections.Generic;
using System.Linq;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Validation.Rules;

/// <summary>
/// 词表注册表（通用）：以「词表键 → 有序词表」的**数据表**实现 <see cref="IPredicateRegistry{TModel}"/> 的受控白名单面。
/// <para>
/// 口径：取值器 / 域谓词**概不注册**（判据只用内核既有封闭节点，不开放表达式 —— 未注册即构筑期失败，
/// 见 <see cref="RuleSpecEngine"/>）；缺失键返回空表（不抛，构筑期由引擎归因）。
/// </para>
/// <para>词表内容的**单源**由消费方保证（如声明面 <c>[Space]</c>），本类不复制任何取值域。</para>
/// </summary>
/// <typeparam name="TModel">领域模型。</typeparam>
public class VocabularyRegistry<TModel> : IPredicateRegistry<TModel>
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _whitelists;

    /// <summary>构筑注册表（空字典 = 无词表，规则引用即构筑期失败）。</summary>
    /// <param name="whitelists">词表键 → 有序词表。</param>
    public VocabularyRegistry(IReadOnlyDictionary<string, IReadOnlyList<string>>? whitelists)
        => _whitelists = whitelists ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    /// <summary>已注册词表键（自检 / 取数；升序）。</summary>
    public IReadOnlyList<string> WhitelistKeys => _whitelists.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <inheritdoc />
    public bool TryGetWhitelist(string key, out IReadOnlyList<string> items)
    {
        if (!string.IsNullOrEmpty(key) && _whitelists.TryGetValue(key, out var found))
        {
            items = found;
            return true;
        }

        items = Array.Empty<string>();
        return false;
    }

    /// <inheritdoc />
    public bool IsRegisteredValueProvider(string providerKind) => false;

    /// <inheritdoc />
    public IReadOnlyList<string> ResolveValues(string providerKind, TModel model, ConversionNode? scopeNode, string? arg)
        => Array.Empty<string>();

    /// <inheritdoc />
    public bool IsRegisteredDomainPredicate(string kind) => false;

    /// <inheritdoc />
    public bool Evaluate(string kind, TModel model, ConversionNode? scopeNode, string arg) => false;
}
