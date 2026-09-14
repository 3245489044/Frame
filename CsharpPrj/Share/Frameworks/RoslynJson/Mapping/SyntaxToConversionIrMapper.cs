
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;
using Share.RoslynJson.Abstractions;
using Share.LogicComposition.Conversion.Diagnostics;
using Share.RoslynJson.Roslyn;
using Share.RoslynJson.Rules;

namespace Share.RoslynJson.Mapping;

/// <summary>
/// 语法节点 → IR 默认映射器：命中规则表即用规则；未命中走兜底映射（kind/text[/位置][/children]）并记警告。
/// 兜底保证「任意语法都能落成 IR」，规则表用于把关心的语法精确化。
/// </summary>
public sealed class SyntaxToConversionIrMapper : ISyntaxToIrMapper
{
    private readonly SyntaxRuleTable _rules;
    private readonly SyntaxMappingOptions _options;

    public SyntaxToConversionIrMapper(SyntaxRuleTable rules, SyntaxMappingOptions? options = null)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _options = options ?? SyntaxMappingOptions.Default;
    }

    public ConversionNode Map(SyntaxNode node, ConversionContext context)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));
        if (context is null) throw new ArgumentNullException(nameof(context));

        return MapCore(node, context, 1);
    }

    /// <summary>
    /// 唯一映射入口：查规则表 → 未命中报 <c>RJ1006</c> → 兜底映射。递归子节点也必经此入口，
    /// 否则规则表对非根节点形同死代码、未映射诊断恒 ≤1 条（S0 spike 实测暴露）。
    /// </summary>
    private ConversionNode MapCore(SyntaxNode node, ConversionContext context, int depth)
    {
        if (_rules.TryGet(node.Kind(), out var mapper))
            return mapper(node, context);

        if (_options.WarnOnUnmapped)
        {
            context.Diagnostics.Report(ConversionDiagnostic.Warning(
                RoslynJsonDiagnosticCodes.MapUnmapped,
                $"语法节点 {node.Kind()} 未注册映射规则，已走兜底映射",
                SourceLocation.ToSpan(node)));
        }

        return MapDefault(node, context, depth);
    }

    private ConversionNode MapDefault(SyntaxNode node, ConversionContext context, int depth)
    {
        var result = new ConversionObject()
            .Add("kind", node.Kind().ToString());

        if (depth > _options.MaxDepth)
        {
            result.Add("truncated", true);
            context.Diagnostics.Report(ConversionDiagnostic.Warning(
                RoslynJsonDiagnosticCodes.MapMaxDepth,
                $"语法映射深度超过上限 {_options.MaxDepth}，已截断子节点",
                SourceLocation.ToSpan(node)));
            return result;
        }

        if (_options.IncludeLocation)
        {
            var span = SourceLocation.ToSpan(node);
            result.Add("line", span.Line);
            result.Add("column", span.Column);
        }

        result.Add("text", node.ToString());

        if (_options.IncludeChildren)
        {
            var children = node.ChildNodes().ToList();
            if (children.Count > 0)
            {
                result.Add("children", new ConversionArray()
                    .AddRange(children.Select(child => MapCore(child, context, depth + 1))));
            }
        }

        return result;
    }
}
