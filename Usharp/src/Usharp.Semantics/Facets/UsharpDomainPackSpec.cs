using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod;
using Share.LogicComposition.Conversion.Validation;

namespace Usharp.Semantics;

/// <summary>
/// 领域包骨架：吸收各 <c>*Pack</c> 重复的 <c>Id</c> / <c>DiagnosticCodePrefixes</c> / 三列表属性，
/// 并承载语法所有权（前缀 / 符号种类）—— 未传前缀的包 = 纯 facet/闸包（不参与编译作用域）。
/// <para>规则执行序 = 传入序；facet provider 序 = 传入序（保诊断归因序）。</para>
/// </summary>
public abstract class UsharpDomainPackSpec : IUsharpGrammarPack
{
    private static readonly IReadOnlyList<string> CodePrefixes = new[] { "US" };

    protected UsharpDomainPackSpec(
        string id,
        IReadOnlyList<IUsharpFacetProvider> providers,
        IReadOnlyList<IGateRule<UsharpGateModel>> rules,
        IReadOnlyList<DslPrefixDef>? prefixes = null,
        IReadOnlyList<DslSymbolKind>? kinds = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        FacetProviders = providers ?? Array.Empty<IUsharpFacetProvider>();
        GateRules = rules ?? Array.Empty<IGateRule<UsharpGateModel>>();
        Prefixes = prefixes ?? Array.Empty<DslPrefixDef>();
        Kinds = kinds ?? Prefixes.Select(p => p.Kind).Distinct().ToArray();
    }

    public string Id { get; }

    public IReadOnlyList<string> DiagnosticCodePrefixes => CodePrefixes;

    public IReadOnlyList<IUsharpFacetProvider> FacetProviders { get; }

    public IReadOnlyList<IGateRule<UsharpGateModel>> GateRules { get; }

    /// <summary>本包私有前缀（默认空 = 不参与编译作用域）。</summary>
    public IReadOnlyList<DslPrefixDef> Prefixes { get; }

    /// <summary>本包消费的符号种类（默认由 <see cref="Prefixes"/> 派生）。</summary>
    public IReadOnlyList<DslSymbolKind> Kinds { get; }
}
