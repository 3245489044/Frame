using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Semantics;

namespace Usharp.Adapters.Agent;

/// <summary>提示词面索引：band 序（= profile bands 并集，保序去重）+ 技能名集。</summary>
public sealed record AgentPromptIndex(IReadOnlyList<string> Bands, IReadOnlyList<string> SkillNames);

/// <summary>
/// 提示词面领域反转包（P5）：把 <c>[Profile].bands</c> + <c>[Skill].dsl/body</c> 汇聚为 band 索引产物
/// （供 <c>prompt.emit</c> 派生提示词文本）；取代 <c>HistoryTaskRuntimePromptProjector</c> 的手写渲染装配面。
/// </summary>
public sealed class AgentPromptPack : UsharpDomainPackSpec
{
    public AgentPromptPack()
        : base("agent.prompt",
            new IUsharpFacetProvider[] { new PromptIndexFacetProvider() },
            Array.Empty<IGateRule<UsharpGateModel>>())
    {
    }
}

/// <summary>提示词面索引 facet（<see cref="AgentPromptIndex"/>）。</summary>
internal sealed class PromptIndexFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(AgentPromptIndex);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Profile, DslSymbolKind.Skill };

    public object Project(UsharpFacetContext ctx)
    {
        var bands = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var decl in ctx.Declarations.OfKind(DslSymbolKind.Profile))
        {
            var p = UsharpProfileDecl.From(decl);
            if (p is null) continue;
            foreach (var b in p.Bands)
                if (seen.Add(b)) bands.Add(b);
        }

        var names = ctx.Declarations.OfKind(DslSymbolKind.Skill)
            .Select(UsharpSkillDecl.From)
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Dsl))
            .Select(s => s!.Name)
            .ToList();

        return new AgentPromptIndex(bands, names);
    }
}
