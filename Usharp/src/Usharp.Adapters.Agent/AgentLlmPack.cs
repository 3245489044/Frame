using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Adapters.Agent;

/// <summary>提示词契约产物（<c>llm.prompt</c>：system/user 提示词**正文文件引用** + 输入字段集 + 采样参数）。
/// 提示词正文不入声明，由相对声明文件的 <c>.md</c> 承载。</summary>
public sealed record LlmPromptContract(
    string Id, string? SystemBody, string? UserBody, IReadOnlyList<string> Inputs, double? Temp, int? MaxCandidates);

/// <summary>输出契约产物（<c>llm.contract</c>：JSON 根 + 字段集 + 目标强类型）。</summary>
public sealed record LlmOutputContract(string Id, string? Root, string? TypeName, IReadOnlyList<string> Fields);

/// <summary>LLM 判定契约（prompt + 输出）——消费方按 id 取用，不感知实现。</summary>
public sealed record LlmContract(LlmPromptContract Prompt, LlmOutputContract Output);

/// <summary>
/// LLM 判定契约领域包（L 系）：把 <c>[LLMDecl]</c> 反转为 facet（契约视图）；<c>US7301</c>–<c>US7304</c>
/// 对账「输出/输入契约 ↔ <c>over=</c> 空间 I/O 区词元」；投影器 <c>llm.prompt</c> / <c>llm.contract</c>
/// 产出可执行提示词与契约化解析描述，取代 <c>HostResourceValueJudge</c>/<c>LlmStateDirector</c> 的
/// 手写 prompt/Parse（只做契约投影，不另立裁决）。
/// </summary>
public sealed class AgentLlmPack : UsharpDomainPackSpec
{
    /// <summary>US7301：<c>[LLMDecl] output=</c> 字段须 ∈ <c>over=</c> 空间 O 区词元。</summary>
    public const string CodeOutputUndeclared = UsharpCodes.LlmContractOutputUndeclared;

    /// <summary>US7302：<c>[LLMDecl] input=</c> 字段须 ∈ <c>over=</c> 空间 I 区词元。</summary>
    public const string CodeInputUndeclared = UsharpCodes.LlmContractInputUndeclared;

    /// <summary>US7303：<c>[LLMDecl]</c> 形态非法（缺 system/output，或 <c>over=</c> 非词元空间）。</summary>
    public const string CodeShapeInvalid = UsharpCodes.LlmDeclShapeInvalid;

    /// <summary>US7304：<c>[LLMDecl] over=</c> 空间未声明。</summary>
    public const string CodeSpaceUnknown = UsharpCodes.LlmDeclSpaceUnknown;

    public AgentLlmPack()
        : base("agent.llm",
            new IUsharpFacetProvider[]
            {
                new UsharpFacetProvider<UsharpLlmDecl>(DslSymbolKind.LlmDecl, ctx =>
                    ctx.Declarations.OfKind(DslSymbolKind.LlmDecl).Select(UsharpLlmDecl.From).OfType<UsharpLlmDecl>()),
            },
            new IGateRule<UsharpGateModel>[] { new LlmContractGateRule() },
            new IUsharpFacetProjector[] { new LlmPromptProjector(), new LlmContractProjector() })
    {
    }
}

/// <summary>US7301–US7304：LLM 判定契约 ↔ 装配对账（判据载体 = 声明名/字段，结构事实）。</summary>
internal sealed class LlmContractGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "agent.llm.contract";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        if (!model.TryGetFacet<IReadOnlyList<UsharpLlmDecl>>(out var decls) || decls.Count == 0) return;

        // [Space] 词元空间索引（I 区 = 输入词元，O 区 = 输出词元）。
        var spaces = new Dictionary<string, UsharpSpaceDecl>(StringComparer.Ordinal);
        foreach (var raw in model.Declarations.OfKind(DslSymbolKind.Space))
        {
            var space = UsharpSpaceDecl.From(raw);
            if (space is not null) spaces[space.SpaceId] = space;
        }

        foreach (var d in decls)
        {
            if (string.IsNullOrWhiteSpace(d.OverSpace))
            {
                sink.Report(ConversionDiagnostic.Error(AgentLlmPack.CodeShapeInvalid,
                    $"[LLMDecl] {d.Name}: 缺 over=<空间>（契约须归入 I/O 词元空间）",
                    new SourceSpan(d.SourceFile, d.LineNumber, 0)));
                continue;
            }

            if (!spaces.TryGetValue(d.OverSpace!, out var space))
            {
                sink.Report(ConversionDiagnostic.Error(AgentLlmPack.CodeSpaceUnknown,
                    $"[LLMDecl] {d.Name}: over='{d.OverSpace}' 空间未在本编译声明面中定义（闸A）",
                    new SourceSpan(d.SourceFile, d.LineNumber, 0)));
                continue;
            }

            if (space.Kind != UsharpSpaceKind.Terms)
            {
                sink.Report(ConversionDiagnostic.Error(AgentLlmPack.CodeShapeInvalid,
                    $"[LLMDecl] {d.Name}: over='{d.OverSpace}' 非词元空间（须 kind=terms）",
                    new SourceSpan(d.SourceFile, d.LineNumber, 0)));
                continue;
            }

            var inTerms = TermsOf(space, "I");
            var outTerms = TermsOf(space, "O");

            if (outTerms.Count == 0)
            {
                sink.Report(ConversionDiagnostic.Error(AgentLlmPack.CodeOutputUndeclared,
                    $"[LLMDecl] {d.Name}: over='{d.OverSpace}' 的输出词元集为空（契约不可闭合；空态不得视为通过）",
                    new SourceSpan(d.SourceFile, d.LineNumber, 0)));
            }
            else
            {
                foreach (var f in d.OutputFields)
                    if (!outTerms.Contains(f))
                        sink.Report(ConversionDiagnostic.Error(AgentLlmPack.CodeOutputUndeclared,
                            $"[LLMDecl] {d.Name}: output 字段 '{f}' 未在 {d.OverSpace} O 区词元中（消手写 Parse 漂移）",
                            new SourceSpan(d.SourceFile, d.LineNumber, 0)));
            }

            if (inTerms.Count == 0)
            {
                sink.Report(ConversionDiagnostic.Error(AgentLlmPack.CodeInputUndeclared,
                    $"[LLMDecl] {d.Name}: over='{d.OverSpace}' 的输入词元集为空（契约不可闭合；空态不得视为通过）",
                    new SourceSpan(d.SourceFile, d.LineNumber, 0)));
            }
            else
            {
                foreach (var f in d.InputFields)
                    if (!inTerms.Contains(f))
                        sink.Report(ConversionDiagnostic.Error(AgentLlmPack.CodeInputUndeclared,
                            $"[LLMDecl] {d.Name}: input 字段 '{f}' 未在 {d.OverSpace} I 区词元中（消手写 BuildPrompt 漂移）",
                            new SourceSpan(d.SourceFile, d.LineNumber, 0)));
            }
        }
    }

    private static HashSet<string> TermsOf(UsharpSpaceDecl space, string zone)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var axis in space.Axes)
            if (string.Equals(axis.Zone, zone, StringComparison.OrdinalIgnoreCase))
                foreach (var term in axis.Terms) set.Add(term);
        return set;
    }
}

/// <summary>投影器 <c>llm.prompt</c>：按 Id 取提示词契约（system/user/inputs/temp）。</summary>
internal sealed class LlmPromptProjector : IUsharpFacetProjector
{
    public string Id => "llm.prompt";
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.LlmDecl };

    public object Emit(UsharpCompilation compilation)
    {
        var map = new Dictionary<string, LlmPromptContract>(StringComparer.Ordinal);
        foreach (var d in LlmDecls(compilation))
            map[d.Name] = new LlmPromptContract(d.Name, d.SystemBody, d.UserBody, d.InputFields, d.Temp, d.MaxCandidates);
        return map;
    }

    /// <summary>契约声明集（投影器共用的 facet 读取单点）。</summary>
    internal static IReadOnlyList<UsharpLlmDecl> LlmDecls(UsharpCompilation compilation)
        => compilation.TryGetFacet<IReadOnlyList<UsharpLlmDecl>>(out var d) ? d : Array.Empty<UsharpLlmDecl>();
}

/// <summary>投影器 <c>llm.contract</c>：按 Id 取完整契约（prompt + 输出 schema）。</summary>
internal sealed class LlmContractProjector : IUsharpFacetProjector
{
    public string Id => "llm.contract";
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.LlmDecl };

    public object Emit(UsharpCompilation compilation)
    {
        var map = new Dictionary<string, LlmContract>(StringComparer.Ordinal);
        foreach (var d in LlmPromptProjector.LlmDecls(compilation))
        {
            var prompt = new LlmPromptContract(d.Name, d.SystemBody, d.UserBody, d.InputFields, d.Temp, d.MaxCandidates);
            var output = new LlmOutputContract(d.Name, d.OutputRoot, d.OutputType, d.OutputFields);
            map[d.Name] = new LlmContract(prompt, output);
        }
        return map;
    }
}
