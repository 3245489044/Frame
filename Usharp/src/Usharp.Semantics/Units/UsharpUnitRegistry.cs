using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>声明单元登记：前缀 + 符号种类 + facet 投影（新增语言单元的唯一样板）。</summary>
public sealed record UsharpUnitSpec<TDecl>(
    string Prefix,
    DslSymbolKind Kind,
    Func<UsharpFacetContext, IEnumerable<TDecl>> Project)
    where TDecl : class
{
    /// <summary>本单元对应的通用 facet provider。</summary>
    public IUsharpFacetProvider Provider => new UsharpFacetProvider<TDecl>(Kind, Project);
}

/// <summary>
/// 语言单元注册表（F1）：把「新增前缀」从 6 处改动收敛为 1 处登记。
/// <para>
/// 序 = 既有编译流序（spaces → obs → Matrix → Flow → Ref → Domain → Gather → Table → Record → Config → Rule →
/// KeySpace → Command），保诊断归因序与产物序。规则集仍由 <see cref="UsharpDomainPack"/> 显式有序持有
/// （规则顺序是诊断契约，不由本表间接决定）。
/// </para>
/// </summary>
public static class UsharpUnitRegistry
{
    /// <summary>核心单元（声明序）。</summary>
    public static readonly IReadOnlyList<(string Prefix, DslSymbolKind Kind)> Units = new[]
    {
        (UsharpLanguage.SpacePrefix, DslSymbolKind.Space),
        (UsharpLanguage.ObsPrefix, DslSymbolKind.Obs),
        (UsharpLanguage.MatrixPrefix, DslSymbolKind.Matrix),
        (UsharpLanguage.FlowPrefix, DslSymbolKind.Flow),
        (UsharpLanguage.RefPrefix, DslSymbolKind.Ref),
        (UsharpLanguage.DomainPrefix, DslSymbolKind.Domain),
        (UsharpLanguage.GatherPrefix, DslSymbolKind.Gather),
        (UsharpLanguage.TablePrefix, DslSymbolKind.Table),
        (UsharpLanguage.RecordPrefix, DslSymbolKind.Record),
        (UsharpLanguage.ConfigPrefix, DslSymbolKind.Config),
        (UsharpLanguage.RulePrefix, DslSymbolKind.Rule),
        (UsharpLanguage.KeySpacePrefix, DslSymbolKind.KeySpace),
        (UsharpLanguage.CommandPrefix, DslSymbolKind.Command),
    };

    /// <summary>默认 facet provider 集（序 = <see cref="Units"/>）。</summary>
    public static readonly IReadOnlyList<IUsharpFacetProvider> Providers = new IUsharpFacetProvider[]
    {
        new SpaceFacetProvider(),
        new ObsFacetProvider(),
        new UsharpFacetProvider<UsharpMatrixDecl>(DslSymbolKind.Matrix,
            ctx => ctx.Documents.SelectMany(UsharpSyntaxProjector.Matrices)),
        new UsharpFacetProvider<UsharpFlowDecl>(DslSymbolKind.Flow,
            ctx => ctx.Documents.SelectMany(UsharpSyntaxProjector.Flows)),
        new UsharpFacetProvider<UsharpRefDecl>(DslSymbolKind.Ref,
            ctx => ctx.Documents.SelectMany(UsharpSyntaxProjector.Refs)),
        new UsharpFacetProvider<UsharpDomainDecl>(DslSymbolKind.Domain,
            ctx => ctx.Documents.SelectMany(UsharpSyntaxProjector.Domains)),
        new UsharpFacetProvider<UsharpGatherDecl>(DslSymbolKind.Gather,
            ctx => ctx.Documents.SelectMany(UsharpSyntaxProjector.Gathers)),
        new UsharpFacetProvider<UsharpTableDecl>(DslSymbolKind.Table,
            ctx => ctx.Documents.SelectMany(UsharpSyntaxProjector.Tables)),
        new UsharpFacetProvider<UsharpRecordDecl>(DslSymbolKind.Record,
            ctx => ctx.Documents.SelectMany(UsharpSyntaxProjector.Records)),
        new UsharpFacetProvider<UsharpConfigDecl>(DslSymbolKind.Config,
            ctx => ctx.Documents.SelectMany(UsharpSyntaxProjector.Configs)),
        new UsharpFacetProvider<UsharpRuleDecl>(DslSymbolKind.Rule,
            ctx => ctx.Documents.SelectMany(UsharpSyntaxProjector.Rules)),
        new UsharpFacetProvider<UsharpKeySpaceDecl>(DslSymbolKind.KeySpace,
            ctx => ctx.Documents.SelectMany(UsharpSyntaxProjector.KeySpaces)),
        new UsharpFacetProvider<UsharpCommandDecl>(DslSymbolKind.Command,
            ctx => ctx.Documents.SelectMany(UsharpSyntaxProjector.Commands)),
    };
}
