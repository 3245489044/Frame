using System.Collections.Generic;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Validation.Rules;

/// <summary>
/// 受控注册端口：**受控取值器**（跨节点取值）与**受控域谓词**（域语义）的注册表 + 白名单词表。
/// <para>
/// 规则表的谓词只能引用**已注册**的取值器 / 域谓词 / 词表键：未注册 ⇒ <c>RuleSpecEngine.Build</c>
/// **构筑期硬失败**（不开放表达式，防配置语言长成第二门编程语言；对齐 RuleSpec §4.2 硬约束）。
/// </para>
/// <para>取值语义由领域包实现（内核只发起调用）；缺失 ⇒ 返回空序列，不抛异常。</para>
/// </summary>
/// <typeparam name="TModel">领域模型。</typeparam>
public interface IPredicateRegistry<in TModel>
{
    /// <summary>取白名单词表（<c>enum-in</c> / <c>key-in-whitelist</c> 的密钥 → 有序词表）。</summary>
    /// <param name="key">词表键（规则表内字面引用）。</param>
    /// <param name="items">out：词表项。</param>
    bool TryGetWhitelist(string key, out IReadOnlyList<string> items);

    /// <summary>取值器是否已注册（构筑期校验）。</summary>
    /// <param name="providerKind">取值器节点类型。</param>
    bool IsRegisteredValueProvider(string providerKind);

    /// <summary>求值取值器（<c>declared-states</c> / <c>declared-modules</c> / <c>chain-derived</c> / <c>prior-knowledge</c> / <c>link</c>）。</summary>
    /// <param name="providerKind">取值器节点类型。</param>
    /// <param name="model">领域模型。</param>
    /// <param name="scopeNode">当前作用域节点。</param>
    /// <param name="arg">取值器实参（如 <c>chain-derived(to)</c> 的 <c>to</c>；无实参为 null）。</param>
    IReadOnlyList<string> ResolveValues(string providerKind, TModel model, ConversionNode? scopeNode, string? arg);

    /// <summary>域谓词是否已注册（构筑期校验）。</summary>
    /// <param name="kind">域谓词节点类型。</param>
    bool IsRegisteredDomainPredicate(string kind);

    /// <summary>求值域谓词（如 <c>has-effect(update)</c>）。</summary>
    /// <param name="kind">域谓词节点类型。</param>
    /// <param name="model">领域模型。</param>
    /// <param name="scopeNode">当前作用域节点。</param>
    /// <param name="arg">域谓词实参。</param>
    bool Evaluate(string kind, TModel model, ConversionNode? scopeNode, string arg);
}
