using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Semantics;

namespace Usharp.Adapters.Agent;

/// <summary>
/// 提示词段声明领域反转包：把 <c>[Segment]</c> 汇聚为 facet（段声明面唯一入口）。
/// <para>与 <see cref="AgentSkillPack"/> 同范式：语法视图在 <see cref="UsharpSegmentDecl"/>，
/// 消费方（段注册 / 契约判定 / 前置门 / 快照）只读本 facet，禁止再解析配置文本。</para>
/// </summary>
public sealed class AgentSegmentPack : IUsharpDomainPack
{
    public AgentSegmentPack()
    {
        FacetProviders = new IUsharpFacetProvider[] { new SegmentDeclFacetProvider() };
        GateRules = Array.Empty<IGateRule<UsharpGateModel>>();
        FacetProjectors = Array.Empty<IUsharpFacetProjector>();
    }

    public string Id => "agent.segment";
    public IReadOnlyList<string> DiagnosticCodePrefixes => new[] { "US" };
    public IReadOnlyList<IUsharpFacetProvider> FacetProviders { get; }
    public IReadOnlyList<IGateRule<UsharpGateModel>> GateRules { get; }
    public IReadOnlyList<IUsharpFacetProjector> FacetProjectors { get; }
}

/// <summary>段声明 facet（<see cref="UsharpSegmentDecl"/> 集；声明行序即登记序）。</summary>
internal sealed class SegmentDeclFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(IReadOnlyList<UsharpSegmentDecl>);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Segment };

    public object Project(UsharpFacetContext ctx)
        => ctx.Declarations.OfKind(DslSymbolKind.Segment)
            .Select(UsharpSegmentDecl.From)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToArray();
}
