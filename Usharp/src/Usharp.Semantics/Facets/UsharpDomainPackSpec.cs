using System;
using System.Collections.Generic;
using Share.LogicComposition.Conversion.Validation;

namespace Usharp.Semantics;

/// <summary>
/// 领域包骨架：吸收各 <c>*Pack</c> 重复的 <c>Id</c> / <c>DiagnosticCodePrefixes</c> / 三列表属性。
/// <para>规则执行序 = 传入序；facet provider 序 = 传入序（保诊断归因序）。</para>
/// </summary>
public abstract class UsharpDomainPackSpec : IUsharpDomainPack
{
    private static readonly IReadOnlyList<string> CodePrefixes = new[] { "US" };

    protected UsharpDomainPackSpec(
        string id,
        IReadOnlyList<IUsharpFacetProvider> providers,
        IReadOnlyList<IGateRule<UsharpGateModel>> rules,
        IReadOnlyList<IUsharpFacetProjector>? projectors = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        FacetProviders = providers ?? Array.Empty<IUsharpFacetProvider>();
        GateRules = rules ?? Array.Empty<IGateRule<UsharpGateModel>>();
        FacetProjectors = projectors ?? Array.Empty<IUsharpFacetProjector>();
    }

    public string Id { get; }

    public IReadOnlyList<string> DiagnosticCodePrefixes => CodePrefixes;

    public IReadOnlyList<IUsharpFacetProvider> FacetProviders { get; }

    public IReadOnlyList<IGateRule<UsharpGateModel>> GateRules { get; }

    public IReadOnlyList<IUsharpFacetProjector> FacetProjectors { get; }
}
