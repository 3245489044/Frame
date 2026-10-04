
using BasicMethod.Initialization.Guards;

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.RoslynJson.Rules;

/// <summary>语法节点映射委托。</summary>
public delegate ConversionNode SyntaxNodeMapper(SyntaxNode node, ConversionContext context);

/// <summary>
/// 语法规则表：<see cref="SyntaxKind"/> → 映射委托。同名 kind 重复注册即失败（单一注册点），
/// 与声明式框架 <c>SpecRegistry.Kind</c> 的「重复注册 fail-fast」一致。
/// </summary>
public sealed class SyntaxRuleTable
{
    private readonly Dictionary<SyntaxKind, SyntaxNodeMapper> _rules = new Dictionary<SyntaxKind, SyntaxNodeMapper>();
    private readonly List<SyntaxKind> _order = new List<SyntaxKind>();

    /// <summary>已注册规则数。</summary>
    public int Count => _rules.Count;

    /// <summary>已注册 kind（按注册顺序，供诊断列出可用集合）。</summary>
    public IReadOnlyList<SyntaxKind> RegisteredKinds => _order;

    /// <summary>注册规则（重复即抛）。</summary>
    public SyntaxRuleTable Register(SyntaxKind kind, SyntaxNodeMapper mapper)
    {
        mapper = PortGuard.Required(mapper, nameof(mapper));
        if (_rules.ContainsKey(kind))
            throw new InvalidOperationException($"SyntaxKind \"{kind}\" 已注册映射规则（单一注册点）");

        _rules.Add(kind, mapper);
        _order.Add(kind);
        return this;
    }

    /// <summary>注册或覆盖规则（派生映射器替换基线时使用）。</summary>
    public SyntaxRuleTable RegisterOrReplace(SyntaxKind kind, SyntaxNodeMapper mapper)
    {
        mapper = PortGuard.Required(mapper, nameof(mapper));

        if (_rules.ContainsKey(kind))
        {
            _rules[kind] = mapper;
            return this;
        }

        return Register(kind, mapper);
    }

    public bool TryGet(SyntaxKind kind, out SyntaxNodeMapper mapper) => _rules.TryGetValue(kind, out mapper!);
}
