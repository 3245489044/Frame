
using System;
using Microsoft.CodeAnalysis;
using Share.LogicComposition.Conversion.Abstractions;
using Share.LogicComposition.Conversion.Emitters;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;
using Share.RoslynJson.Abstractions;
using Share.RoslynJson.Emitters;
using Share.RoslynJson.Mapping;
using Share.RoslynJson.Roslyn;
using Share.RoslynJson.Rules;

namespace Share.RoslynJson.Service;

/// <summary>
/// Roslyn→JSON 转换服务（框架默认装配入口）：源文本 → 语法树 → IR → JSON / C# 内嵌。
/// 三个可选注入点（源提供器 / 映射器 / 管线）便于派生与测试替换。
/// </summary>
public sealed class RoslynJsonConversionService
{
    private readonly IRoslynSourceProvider _sourceProvider;
    private readonly ISyntaxToIrMapper _mapper;
    private readonly IConversionPipeline? _pipeline;

    public RoslynJsonConversionService(
        IRoslynSourceProvider? sourceProvider = null,
        ISyntaxToIrMapper? mapper = null,
        IConversionPipeline? pipeline = null)
    {
        _sourceProvider = sourceProvider ?? CSharpSourceProvider.Instance;
        _mapper = mapper ?? new SyntaxToConversionIrMapper(new SyntaxRuleTable(), SyntaxMappingOptions.Default);
        _pipeline = pipeline;
    }

    /// <summary>源文本 → IR（含语法错误转诊断；管线可选）。</summary>
    public ConversionResult<ConversionNode> ToIr(string sourceText, string filePath, ConversionContext? context = null)
    {
        if (sourceText is null) throw new ArgumentNullException(nameof(sourceText));
        if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("源文件路径不能为空", nameof(filePath));

        var ctx = context ?? new ConversionContext(ConversionOptions.Default, filePath, sourceText);

        var tree = _sourceProvider.Parse(sourceText, filePath);
        ReportParseDiagnostics(tree, ctx);

        if (ctx.Diagnostics.HasErrors)
            return ConversionResult<ConversionNode>.Failure(ctx.Diagnostics.Diagnostics);

        var ir = _mapper.Map(_sourceProvider.GetRoot(tree), ctx);

        if (_pipeline is null)
        {
            return ctx.Diagnostics.HasErrors
                ? ConversionResult<ConversionNode>.Failure(ctx.Diagnostics.Diagnostics, ir)
                : ConversionResult<ConversionNode>.Success(ir, ctx.Diagnostics.Diagnostics);
        }

        return _pipeline.Run(ir, ctx);
    }

    /// <summary>源文本 → JSON 文本。</summary>
    public ConversionResult<string> ToJson(string sourceText, string filePath, ConversionOptions? options = null)
    {
        var opts = options ?? ConversionOptions.Default;
        var ctx = new ConversionContext(opts, filePath, sourceText);

        var ir = ToIr(sourceText, filePath, ctx);
        if (!ir.IsSuccess || ir.Artifact is null)
            return ConversionResult<string>.Failure(ir.Diagnostics);

        var json = JsonTextEmitter.Instance.Emit(ir.Artifact, opts);
        return json;
    }

    /// <summary>源文本 → C#（内嵌 JSON 的类）。</summary>
    public ConversionResult<string> ToEmbeddedCSharp(
        string sourceText,
        string filePath,
        EmbeddedJsonClassSpec spec,
        ConversionOptions? options = null)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));

        var opts = options ?? ConversionOptions.Default;
        var ctx = new ConversionContext(opts, filePath, sourceText);

        var ir = ToIr(sourceText, filePath, ctx);
        if (!ir.IsSuccess || ir.Artifact is null)
            return ConversionResult<string>.Failure(ir.Diagnostics);

        return CSharpEmbeddedJsonEmitter.Instance.Emit(ir.Artifact, spec, opts);
    }

    private static void ReportParseDiagnostics(SyntaxTree tree, ConversionContext context)
    {
        foreach (var diagnostic in tree.GetDiagnostics())
        {
            var severity = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => ConversionSeverity.Error,
                DiagnosticSeverity.Warning => ConversionSeverity.Warning,
                _ => ConversionSeverity.Info,
            };

            context.Diagnostics.Report(new ConversionDiagnostic(
                severity,
                diagnostic.Id,
                diagnostic.GetMessage(),
                SourceLocation.ToSpan(diagnostic)));
        }
    }
}
