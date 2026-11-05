using System;
using System.Collections.Generic;

namespace Share.LogicComposition.Conversion.Validation.Rules;

/// <summary>谓词实参槽位角色（位置即语义；构筑期按角色校验）。</summary>
public static class PredicateRole
{
    /// <summary>路径（须符合 <see cref="RulePath"/> 文法）。</summary>
    public const string Path = "path";

    /// <summary>路径或字面量（右式可回落字面量语义）。</summary>
    public const string PathOrLiteral = "path|literal";

    /// <summary>白名单词表键（须已注册）。</summary>
    public const string Whitelist = "whitelist";

    /// <summary>集合实参：路径 <b>或</b> 集合表达式（取值器 / <c>union</c>）。</summary>
    public const string Set = "set";

    /// <summary>集合表达式（只能为取值器 / <c>union</c>）。</summary>
    public const string SetExpr = "setExpr";
}

/// <summary>
/// 谓词节点白名单条目：节点类型 = 封闭集（无变量 / 无循环 / 无宏 / 无条件关键字）。
/// <para>
/// 只描述**槽位形状**（操作数角色 / 子表达式数 / 数值实参 / 具名实参键），不描述求值语义；
/// 求值语义在唯一解释内核内，本条目不含领域知识。
/// </para>
/// </summary>
public sealed class PredicateNodeSpec
{
    /// <summary>节点类型名（白名单键）。</summary>
    public string Kind { get; }

    /// <summary>操作数角色序（**位置即语义**；长度即操作数个数）。</summary>
    public IReadOnlyList<string> OperandRoles { get; }

    /// <summary>子表达式下限。</summary>
    public int MinChildren { get; }

    /// <summary>子表达式上限（<see cref="int.MaxValue"/> = 不限）。</summary>
    public int MaxChildren { get; }

    /// <summary>是否要求数值实参（如 <c>count-gt(path, n)</c> 的 <c>n</c>）。</summary>
    public bool RequiresNumber { get; }

    /// <summary>允许的具名实参键（空 = 不允许具名实参）。</summary>
    public IReadOnlyList<string> NamedArgKeys { get; }

    /// <summary>操作数个数（= 角色序长度）。</summary>
    public int OperandCount => OperandRoles.Count;

    /// <summary>取槽位角色（越界返回 null）。</summary>
    /// <param name="index">槽位序号。</param>
    public string? RoleAt(int index)
        => index >= 0 && index < OperandRoles.Count ? OperandRoles[index] : null;

    /// <summary>构筑白名单条目。</summary>
    /// <param name="kind">节点类型。</param>
    /// <param name="operandRoles">操作数角色序。</param>
    /// <param name="minChildren">子表达式下限。</param>
    /// <param name="maxChildren">子表达式上限。</param>
    /// <param name="requiresNumber">是否要求数值实参。</param>
    /// <param name="namedArgKeys">允许的具名实参键。</param>
    public PredicateNodeSpec(
        string kind,
        string[] operandRoles,
        int minChildren = 0,
        int maxChildren = 0,
        bool requiresNumber = false,
        string[]? namedArgKeys = null)
    {
        Kind = kind ?? throw new ArgumentNullException(nameof(kind));
        OperandRoles = operandRoles ?? Array.Empty<string>();
        MinChildren = minChildren;
        MaxChildren = maxChildren;
        RequiresNumber = requiresNumber;
        NamedArgKeys = namedArgKeys ?? Array.Empty<string>();
    }
}

/// <summary>
/// 谓词节点白名单（**唯一真源**）：规则表可出现的全部节点类型 + 受控控制流关键字拒绝集。
/// <para>
/// 封闭性硬约束（对齐 RuleSpec §4.2）：无变量 / 无循环 / 无宏 / 无条件关键字；
/// <c>all-of</c> / <c>any-of</c> / <c>not</c> 属**谓词代数**节点（无求值顺序与作用域），不属禁用范畴；
/// 未知节点类型 / 未知具名实参键 / 未注册白名单键一律**构筑期硬失败**（不降级、不静默）。
/// </para>
/// </summary>
public static class PredicateCatalog
{
    /// <summary>节点：路径存在。</summary>
    public const string Present = "present";
    /// <summary>节点：路径缺失。</summary>
    public const string Absent = "absent";
    /// <summary>节点：非空。</summary>
    public const string NonEmpty = "non-empty";
    /// <summary>节点：空。</summary>
    public const string Empty = "empty";
    /// <summary>节点：集合元素数大于 n。</summary>
    public const string CountGt = "count-gt";
    /// <summary>节点：等值（右式 = 字面量或路径）。</summary>
    public const string Equals_ = "equals";
    /// <summary>节点：取值落在白名单词表内。</summary>
    public const string EnumIn = "enum-in";
    /// <summary>节点：持有者键落在白名单词表内。</summary>
    public const string KeyInWhitelist = "key-in-whitelist";
    /// <summary>节点：集合按键唯一。</summary>
    public const string Unique = "unique";
    /// <summary>节点：两集合相交非空。</summary>
    public const string Intersects = "intersects";
    /// <summary>节点：两集合并集（集合位置）。</summary>
    public const string Union = "union";
    /// <summary>节点：引用须落在已声明集。</summary>
    public const string RefDeclared = "ref-declared";
    /// <summary>节点：合取（谓词代数）。</summary>
    public const string AllOf = "all-of";
    /// <summary>节点：析取（谓词代数）。</summary>
    public const string AnyOf = "any-of";
    /// <summary>节点：否定（谓词代数）。</summary>
    public const string Not = "not";
    /// <summary>取值器：已声明状态集。</summary>
    public const string DeclaredStates = "declared-states";
    /// <summary>取值器：已声明模块集。</summary>
    public const string DeclaredModules = "declared-modules";
    /// <summary>取值器：链上派生的目标集。</summary>
    public const string ChainDerived = "chain-derived";
    /// <summary>取值器：先验知识集。</summary>
    public const string PriorKnowledge = "prior-knowledge";
    /// <summary>取值器：按类型取连接集。</summary>
    public const string Link = "link";
    /// <summary>域谓词：更新体有实际效果。</summary>
    public const string HasEffect = "has-effect";

    /// <summary>谓词节点类型的声明序（稳定：白名单列举与诊断消息一律用此序）。</summary>
    public static readonly IReadOnlyList<string> OrderedKinds = new[]
    {
        Present, Absent, NonEmpty, Empty, CountGt,
        Equals_, EnumIn, KeyInWhitelist,
        Unique, Intersects, Union,
        RefDeclared,
        AllOf, AnyOf, Not,
        DeclaredStates, DeclaredModules, ChainDerived, PriorKnowledge, Link,
        HasEffect,
    };

    /// <summary>
    /// 受控控制流关键字拒绝集（封闭白名单的**显式**表达，防「配置语言长成第二门编程语言」）。
    /// </summary>
    public static readonly IReadOnlyList<string> ReservedKeywords = new[]
    {
        "if", "else", "for", "while", "switch", "loop", "macro", "def", "let", "var", "return",
    };

    private static readonly Dictionary<string, PredicateNodeSpec> Specs = BuildSpecs();

    /// <summary>取白名单条目（未知类型 = false；调用方转构筑期硬失败）。</summary>
    /// <param name="kind">节点类型。</param>
    /// <param name="spec">out：白名单条目。</param>
    public static bool TryGetSpec(string? kind, out PredicateNodeSpec spec)
    {
        spec = null!;
        return !string.IsNullOrWhiteSpace(kind) && Specs.TryGetValue(kind!, out spec!);
    }

    /// <summary>是否为受控控制流关键字（Ordinal 精确匹配）。</summary>
    /// <param name="name">待判名称。</param>
    public static bool IsReservedKeyword(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        for (var i = 0; i < ReservedKeywords.Count; i++)
        {
            if (string.Equals(ReservedKeywords[i], name, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>取节点槽位角色序（未知类型 = 空列表）。</summary>
    /// <param name="kind">节点类型。</param>
    public static IReadOnlyList<string> SlotRolesOf(string? kind)
        => !string.IsNullOrWhiteSpace(kind) && Specs.TryGetValue(kind!, out var spec)
            ? spec.OperandRoles
            : Array.Empty<string>();

    private static Dictionary<string, PredicateNodeSpec> BuildSpecs()
    {
        var specs = new Dictionary<string, PredicateNodeSpec>(StringComparer.Ordinal)
        {
            [Present] = new PredicateNodeSpec(Present, new[] { PredicateRole.Path }),
            [Absent] = new PredicateNodeSpec(Absent, new[] { PredicateRole.Path }),
            [NonEmpty] = new PredicateNodeSpec(NonEmpty, new[] { PredicateRole.Path }),
            [Empty] = new PredicateNodeSpec(Empty, new[] { PredicateRole.Path }),
            [CountGt] = new PredicateNodeSpec(CountGt, new[] { PredicateRole.Path }, requiresNumber: true),
            [Equals_] = new PredicateNodeSpec(Equals_, new[] { PredicateRole.Path, PredicateRole.PathOrLiteral }),
            [EnumIn] = new PredicateNodeSpec(EnumIn, new[] { PredicateRole.Path, PredicateRole.Whitelist }),
            [KeyInWhitelist] = new PredicateNodeSpec(KeyInWhitelist, new[] { PredicateRole.Path, PredicateRole.Whitelist }),
            [Unique] = new PredicateNodeSpec(Unique, new[] { PredicateRole.Path, PredicateRole.Path }),
            [Intersects] = new PredicateNodeSpec(Intersects, new[] { PredicateRole.Set, PredicateRole.Set }),
            [Union] = new PredicateNodeSpec(Union, new[] { PredicateRole.Set, PredicateRole.Set }),
            [RefDeclared] = new PredicateNodeSpec(RefDeclared, new[] { PredicateRole.Path, PredicateRole.Set }),
            [AllOf] = new PredicateNodeSpec(AllOf, Array.Empty<string>(), minChildren: 1, maxChildren: int.MaxValue),
            [AnyOf] = new PredicateNodeSpec(AnyOf, Array.Empty<string>(), minChildren: 1, maxChildren: int.MaxValue),
            [Not] = new PredicateNodeSpec(Not, Array.Empty<string>(), minChildren: 1, maxChildren: 1),
            [DeclaredStates] = new PredicateNodeSpec(DeclaredStates, Array.Empty<string>()),
            [DeclaredModules] = new PredicateNodeSpec(DeclaredModules, Array.Empty<string>()),
            [ChainDerived] = new PredicateNodeSpec(ChainDerived, new[] { PredicateRole.Path }),
            [PriorKnowledge] = new PredicateNodeSpec(PriorKnowledge, Array.Empty<string>()),
            [Link] = new PredicateNodeSpec(Link, Array.Empty<string>(), namedArgKeys: new[] { "type" }),
            [HasEffect] = new PredicateNodeSpec(HasEffect, new[] { PredicateRole.Path }),
        };

        // 自检：白名单须与声明序一一对应（防「登记了却不可枚举 / 可枚举却未登记」）。
        if (specs.Count != OrderedKinds.Count)
        {
            throw new InvalidOperationException(
                "谓词白名单不一致：Specs=" + specs.Count + " / OrderedKinds=" + OrderedKinds.Count);
        }

        for (var i = 0; i < OrderedKinds.Count; i++)
        {
            if (!specs.ContainsKey(OrderedKinds[i]))
            {
                throw new InvalidOperationException("谓词白名单缺少声明序条目：" + OrderedKinds[i]);
            }
        }

        return specs;
    }
}
