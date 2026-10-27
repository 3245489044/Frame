using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod;
using Plate.DslMethod.Model;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Semantics;

namespace Usharp.Adapters.Agent;

/// <summary>工具面引用边（<c>[Ref] … | edge=hosts|feeds | src=&lt;face&gt; | tgt=&lt;…&gt;</c>；声明序）。</summary>
public sealed record AgentToolFaceEdge(string Face, string Edge, string Target, string SourceFile, int LineNumber);

/// <summary>
/// 工具面（MCP）领域反转包：把 <c>tool-face.ushar</c> 的「面→轨 / 面→源」引用反转为 facet，
/// 并以 <c>US7004</c> 对账「声明轨 id ∈ 实现轨注册表」——不一致即**编译期红**，取代运行期静默漂移。
/// </summary>
public sealed class AgentToolPack : UsharpDomainPackSpec
{
    /// <summary>US7605：<c>edge=hosts</c> 的轨 id 须 ∈ 轨实现注册表。</summary>
    public const string CodeTrackUnknown = UsharpCodes.ToolTrackUnknown;

    /// <summary>面→轨 边类型（单一真源 = Agent.Launcher ToolFaceDeclaration.EdgeHosts 同值）。</summary>
    public const string EdgeHosts = "hosts";

    /// <summary>面→源 边类型。</summary>
    public const string EdgeFeeds = "feeds";

    public AgentToolPack(IReadOnlyCollection<string>? knownTracks = null)
        : base("agent.tool",
            new IUsharpFacetProvider[] { new ToolFaceFacetProvider() },
            new IGateRule<UsharpGateModel>[] { new ToolFaceTrackGateRule(knownTracks ?? Array.Empty<string>()) },
            new IUsharpFacetProjector[] { new ToolFaceProjector() })
    {
    }
}

/// <summary>工具面边 facet（<c>IReadOnlyList&lt;AgentToolFaceEdge&gt;</c>）。</summary>
internal sealed class ToolFaceFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(IReadOnlyList<AgentToolFaceEdge>);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Ref };

    public object Project(UsharpFacetContext ctx)
    {
        var edges = new List<AgentToolFaceEdge>();
        foreach (var decl in ctx.Declarations.OfKind(DslSymbolKind.Ref))
        {
            var reference = Usharp.Syntax.UsharpRefDecl.From(decl);
            if (reference is null) continue;
            var edge = reference.Edge;
            if (edge is null) continue;
            if (!string.Equals(edge, AgentToolPack.EdgeHosts, StringComparison.Ordinal)
                && !string.Equals(edge, AgentToolPack.EdgeFeeds, StringComparison.Ordinal)) continue;
            if (string.IsNullOrWhiteSpace(reference.Source) || string.IsNullOrWhiteSpace(reference.Target)) continue;

            edges.Add(new AgentToolFaceEdge(reference.Source!, edge, reference.Target!, reference.SourceFile, reference.LineNumber));
        }
        return edges;
    }
}

/// <summary>US7004：<c>edge=hosts</c> 的轨 id 须 ∈ 轨实现注册表（未注入 ⇒ 不判定）。</summary>
internal sealed class ToolFaceTrackGateRule : IGateRule<UsharpGateModel>
{
    private readonly HashSet<string> _knownTracks;

    public ToolFaceTrackGateRule(IReadOnlyCollection<string> knownTracks)
        => _knownTracks = new HashSet<string>(knownTracks.Select(Normalize), StringComparer.Ordinal);

    public string Name => "agent.tool.track";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        if (_knownTracks.Count == 0) return;
        if (!model.TryGetFacet<IReadOnlyList<AgentToolFaceEdge>>(out var edges)) return;

        foreach (var e in edges)
        {
            if (!string.Equals(e.Edge, AgentToolPack.EdgeHosts, StringComparison.Ordinal)) continue;
            if (_knownTracks.Contains(Normalize(e.Target))) continue;
            sink.Report(ConversionDiagnostic.Error(AgentToolPack.CodeTrackUnknown,
                $"[Ref] {e.Face}: 引用了未注册轨 '{e.Target}'（闸A：声明↔实现对账）",
                new SourceSpan(e.SourceFile, e.LineNumber, 0)));
        }
    }

    private static string Normalize(string trackId) => trackId.Replace('-', '_');
}

/// <summary>投影器 <c>tool.face</c>：工具面边产物（面→轨 / 面→源）。</summary>
internal sealed class ToolFaceProjector : IUsharpFacetProjector
{
    public string Id => "tool.face";
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Ref };

    public object Emit(UsharpCompilation compilation) => compilation.Facet<IReadOnlyList<AgentToolFaceEdge>>();
}
