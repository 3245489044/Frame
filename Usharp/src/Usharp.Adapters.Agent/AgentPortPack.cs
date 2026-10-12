using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Semantics;

namespace Usharp.Adapters.Agent;

/// <summary>端口节点（<c>[Node] Name: fn=… | inports=… | outports=…</c>）。</summary>
public sealed record AgentPortNode(
    string Name, string? Fn, IReadOnlyList<string> Inports, IReadOnlyList<string> Outports, string SourceFile, int LineNumber);

/// <summary>
/// 端口面领域反转包（P5）：<c>[Node]</c> 端口节点反转为 facet；<c>US7003</c> 对账 <c>fn ∈ 函数注册表</c>
/// （消 <c>IHistoryRuntimePort</c> 手写契约的运行期漂移）。
/// </summary>
public sealed class AgentPortPack : IUsharpDomainPack
{
    /// <summary>US7003：<c>[Node] fn=</c> 须 ∈ 函数注册表。</summary>
    public const string CodeFnUnknown = "US7003";

    public AgentPortPack(IReadOnlyCollection<string>? knownFunctions = null)
    {
        FacetProviders = new IUsharpFacetProvider[] { new PortNodeFacetProvider() };
        GateRules = new IGateRule<UsharpGateModel>[] { new PortFnGateRule(knownFunctions ?? Array.Empty<string>()) };
        FacetProjectors = new IUsharpFacetProjector[] { new PortContractProjector() };
    }

    public string Id => "agent.port";
    public IReadOnlyList<string> DiagnosticCodePrefixes => new[] { "US" };
    public IReadOnlyList<IUsharpFacetProvider> FacetProviders { get; }
    public IReadOnlyList<IGateRule<UsharpGateModel>> GateRules { get; }
    public IReadOnlyList<IUsharpFacetProjector> FacetProjectors { get; }
}

/// <summary>端口节点 facet（<c>IReadOnlyList&lt;AgentPortNode&gt;</c>；声明序）。</summary>
internal sealed class PortNodeFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(IReadOnlyList<AgentPortNode>);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Node };

    public object Project(UsharpFacetContext ctx)
        => ctx.Declarations.OfKind(DslSymbolKind.Node)
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .Select(d => new AgentPortNode(
                d.Name!,
                NullIfEmpty(d.Field("fn")),
                SplitList(d.Field("inports")),
                SplitList(d.Field("outports")),
                d.SourceFile, d.LineNumber))
            .ToList();

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> SplitList(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>US7003：<c>[Node] fn=</c> 须 ∈ 函数注册表（未注入 ⇒ 不判定）。</summary>
internal sealed class PortFnGateRule : IGateRule<UsharpGateModel>
{
    private readonly HashSet<string> _knownFunctions;

    public PortFnGateRule(IReadOnlyCollection<string> knownFunctions)
        => _knownFunctions = new HashSet<string>(knownFunctions, StringComparer.Ordinal);

    public string Name => "agent.port.fn";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        if (_knownFunctions.Count == 0) return;
        if (!model.TryGetFacet<IReadOnlyList<AgentPortNode>>(out var nodes)) return;

        foreach (var n in nodes)
        {
            if (string.IsNullOrWhiteSpace(n.Fn)) continue;
            if (_knownFunctions.Contains(n.Fn!)) continue;
            sink.Report(ConversionDiagnostic.Error(AgentPortPack.CodeFnUnknown,
                $"[Node] {n.Name}: fn='{n.Fn}' 未在函数注册表中（闸A：声明↔实现对账）",
                new SourceSpan(n.SourceFile, n.LineNumber, 0)));
        }
    }
}

/// <summary>投影器 <c>port.contract</c>：端口契约产物（节点 fn/inports/outports）。</summary>
internal sealed class PortContractProjector : IUsharpFacetProjector
{
    public string Id => "port.contract";
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Node };

    public object Emit(UsharpCompilation compilation) => compilation.Facet<IReadOnlyList<AgentPortNode>>();
}
