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
public sealed class AgentSegmentPack : UsharpDomainPackSpec
{
    public AgentSegmentPack()
        : base("agent.segment",
            new IUsharpFacetProvider[]
            {
                new UsharpFacetProvider<UsharpSegmentDecl>(DslSymbolKind.Segment, ctx =>
                    ctx.Declarations.OfKind(DslSymbolKind.Segment).Select(UsharpSegmentDecl.From).OfType<UsharpSegmentDecl>()),
            },
            Array.Empty<IGateRule<UsharpGateModel>>(),
            Array.Empty<IUsharpFacetProjector>())
    {
    }
}

