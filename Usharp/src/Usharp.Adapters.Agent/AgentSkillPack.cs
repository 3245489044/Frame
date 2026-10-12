using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Semantics;

namespace Usharp.Adapters.Agent;

/// <summary>
/// 技能 / 档案领域反转包：把 <c>[Skill]</c>（唯一新增语言单元）与 <c>[Profile]</c>（核心前缀，消费化）
/// 绑为可注册单元 —— facet provider（元数据视图）+ 闸规则（声明↔实现对账）+ facet 投影器（提示词产物）。
/// </summary>
public sealed class AgentSkillPack : IUsharpDomainPack
{
    /// <summary>US7001：<c>[Skill] backend=</c> 须 ∈ 工具注册表。</summary>
    public const string CodeBackendUnknown = "US7001";

    /// <summary>US7002：<c>[Profile] bands=</c> 须 ∈ 已声明技能的 dsl 集。</summary>
    public const string CodeBandUndeclared = "US7002";

    public AgentSkillPack(
        IReadOnlyCollection<string>? knownTools = null,
        IReadOnlyCollection<string>? knownBands = null)
    {
        FacetProviders = new IUsharpFacetProvider[]
        {
            new SkillFacetProvider(),
            new ProfileFacetProvider(),
        };

        GateRules = new IGateRule<UsharpGateModel>[]
        {
            new SkillBackendGateRule(knownTools ?? Array.Empty<string>()),
            new ProfileBandGateRule(knownBands ?? Array.Empty<string>()),
        };

        FacetProjectors = new IUsharpFacetProjector[]
        {
            new PromptSkillsProjector(),
            new PromptBandsProjector(),
        };
    }

    public string Id => "agent.skill";

    public IReadOnlyList<string> DiagnosticCodePrefixes => new[] { "US" };

    public IReadOnlyList<IUsharpFacetProvider> FacetProviders { get; }

    public IReadOnlyList<IGateRule<UsharpGateModel>> GateRules { get; }

    public IReadOnlyList<IUsharpFacetProjector> FacetProjectors { get; }
}

/// <summary><c>[Skill]</c> 元数据 facet（<c>IReadOnlyList&lt;UsharpSkillDecl&gt;</c>）。</summary>
internal sealed class SkillFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(IReadOnlyList<UsharpSkillDecl>);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Skill };

    public object Project(UsharpFacetContext ctx)
        => ctx.Declarations.OfKind(DslSymbolKind.Skill)
            .Select(UsharpSkillDecl.From)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
}

/// <summary><c>[Profile]</c> 档案 facet（<c>IReadOnlyList&lt;UsharpProfileDecl&gt;</c>）。</summary>
internal sealed class ProfileFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(IReadOnlyList<UsharpProfileDecl>);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Profile };

    public object Project(UsharpFacetContext ctx)
        => ctx.Declarations.OfKind(DslSymbolKind.Profile)
            .Select(UsharpProfileDecl.From)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
}

/// <summary>US7001：<c>[Skill] backend=</c> 须 ∈ 工具注册表（未注入注册表 ⇒ 不判定，避免误红）。</summary>
internal sealed class SkillBackendGateRule : IGateRule<UsharpGateModel>
{
    private readonly IReadOnlyCollection<string> _knownTools;

    public SkillBackendGateRule(IReadOnlyCollection<string> knownTools) => _knownTools = knownTools;

    public string Name => "agent.skill.backend";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        if (_knownTools.Count == 0) return;
        if (!model.TryGetFacet<IReadOnlyList<UsharpSkillDecl>>(out var skills)) return;

        foreach (var s in skills)
        {
            if (string.IsNullOrWhiteSpace(s.Backend)) continue;
            if (_knownTools.Contains(s.Backend!, StringComparer.Ordinal)) continue;
            sink.Report(ConversionDiagnostic.Error(AgentSkillPack.CodeBackendUnknown,
                $"[Skill] {s.Name}: backend='{s.Backend}' 未在工具注册表中（闸A：声明↔实现对账）",
                new SourceSpan(s.SourceFile, s.LineNumber, 0)));
        }
    }
}

/// <summary>US7002：<c>[Profile] bands=</c> 每项须 ∈ 已声明技能的 <c>dsl</c> 集（未注入白名单 ⇒ 不判定）。</summary>
internal sealed class ProfileBandGateRule : IGateRule<UsharpGateModel>
{
    private readonly IReadOnlyCollection<string> _knownBands;

    public ProfileBandGateRule(IReadOnlyCollection<string> knownBands) => _knownBands = knownBands;

    public string Name => "agent.skill.band";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        if (!model.TryGetFacet<IReadOnlyList<UsharpProfileDecl>>(out var profiles)) return;
        if (!model.TryGetFacet<IReadOnlyList<UsharpSkillDecl>>(out var skills)) skills = Array.Empty<UsharpSkillDecl>();

        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in skills)
            if (!string.IsNullOrWhiteSpace(s.Dsl)) declared.Add(s.Dsl!);
        foreach (var b in _knownBands) declared.Add(b);
        if (declared.Count == 0) return;

        foreach (var p in profiles)
            foreach (var band in p.Bands)
                if (!declared.Contains(band))
                    sink.Report(ConversionDiagnostic.Error(AgentSkillPack.CodeBandUndeclared,
                        $"[Profile] {p.Key}: band='{band}' 未在已声明技能的 dsl 集内（闸A）",
                        new SourceSpan(p.SourceFile, p.LineNumber, 0)));
    }
}

/// <summary>投影器 <c>prompt.skills</c>：技能元数据产物。</summary>
internal sealed class PromptSkillsProjector : IUsharpFacetProjector
{
    public string Id => "prompt.skills";
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Skill };

    public object Emit(UsharpCompilation compilation) => compilation.Facet<IReadOnlyList<UsharpSkillDecl>>();
}

/// <summary>投影器 <c>prompt.bands</c>：档案 band 序产物。</summary>
internal sealed class PromptBandsProjector : IUsharpFacetProjector
{
    public string Id => "prompt.bands";
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Profile };

    public object Emit(UsharpCompilation compilation) => compilation.Facet<IReadOnlyList<UsharpProfileDecl>>();
}
