using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Share.LogicComposition.Conversion.Vocabulary;
using Usharp.Semantics;

namespace Usharp.Adapters.Agent;

/// <summary>
/// 技能 / 档案领域反转包：把 <c>[Skill]</c>（唯一新增语言单元）与 <c>[Profile]</c>（核心前缀，消费化）
/// 绑为可注册单元 —— facet provider（元数据视图）+ 闸规则（声明↔实现对账）+ facet 投影器（提示词产物）。
/// </summary>
public sealed class AgentSkillPack : UsharpDomainPackSpec
{
    /// <summary>US7601：<c>[Skill] backend=</c> 须 ∈ 工具注册表。</summary>
    public const string CodeBackendUnknown = UsharpCodes.SkillBackendUnknown;

    /// <summary>US7602：<c>[Profile] bands=</c> 须 ∈ 已声明技能的 dsl 集。</summary>
    public const string CodeBandUndeclared = UsharpCodes.SkillBandUndeclared;

    /// <summary>US7603：<c>[Profile] base=on</c> 的框架常驻段声明须 <c>bands</c> 非空。</summary>
    public const string CodeBaseBandMissing = UsharpCodes.SkillBaseBandMissing;

    public AgentSkillPack(
        IReadOnlyCollection<string>? knownTools = null,
        IReadOnlyCollection<string>? knownBands = null)
        : base("agent.skill",
            new IUsharpFacetProvider[]
            {
                new UsharpFacetProvider<UsharpSkillDecl>(DslSymbolKind.Skill, ctx =>
                    ctx.Declarations.OfKind(DslSymbolKind.Skill).Select(UsharpSkillDecl.From).OfType<UsharpSkillDecl>()),
                new UsharpFacetProvider<UsharpProfileDecl>(DslSymbolKind.Profile, ctx =>
                    ctx.Declarations.OfKind(DslSymbolKind.Profile).Select(UsharpProfileDecl.From).OfType<UsharpProfileDecl>()),
            },
            new IGateRule<UsharpGateModel>[]
            {
                new SkillBackendGateRule(knownTools ?? Array.Empty<string>()),
                new ProfileBandGateRule(knownBands ?? Array.Empty<string>()),
                new ProfileBaseGateRule(),
            })
    {
    }
}

/// <summary>US7601：<c>[Skill] backend=</c> 须 ∈ 工具注册表（未注入注册表 ⇒ 不判定，避免误红）。</summary>
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

        var declared = new OrderedWhitelist(
            skills.Where(s => !string.IsNullOrWhiteSpace(s.Dsl)).Select(s => s.Dsl!),
            _knownBands);
        if (declared.Items.Count == 0) return;

        foreach (var p in profiles)
            foreach (var band in p.Bands)
                if (!declared.Contains(band))
                    sink.Report(ConversionDiagnostic.Error(AgentSkillPack.CodeBandUndeclared,
                        $"[Profile] {p.Key}: band='{band}' 未在已声明技能的 dsl 集内（闸A）",
                        new SourceSpan(p.SourceFile, p.LineNumber, 0)));
    }
}

/// <summary>US7005：<c>[Profile] base=on</c> 的框架常驻段声明须 <c>bands</c> 非空。</summary>
internal sealed class ProfileBaseGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "agent.skill.base";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        if (!model.TryGetFacet<IReadOnlyList<UsharpProfileDecl>>(out var profiles)) return;

        foreach (var p in profiles)
        {
            if (!p.Base) continue;
            if (p.Bands.Count > 0) continue;
            sink.Report(ConversionDiagnostic.Error(AgentSkillPack.CodeBaseBandMissing,
                $"[Profile] {p.Key}: base=on 但 bands 为空（框架常驻基础段须声明段集）",
                new SourceSpan(p.SourceFile, p.LineNumber, 0)));
        }
    }
}
