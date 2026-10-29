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
        (UsharpLanguage.ReadModelPrefix, DslSymbolKind.ReadModel),
        (UsharpLanguage.RelationPrefix, DslSymbolKind.Relation),
        (UsharpLanguage.InvariantPrefix, DslSymbolKind.Invariant),
        (UsharpLanguage.DerivePrefix, DslSymbolKind.Derive),
        (UsharpLanguage.MountPrefix, DslSymbolKind.Mount),
        (UsharpLanguage.SegmentPrefix, DslSymbolKind.Segment),
        (UsharpLanguage.EntityPrefix, DslSymbolKind.Entity),
        (UsharpLanguage.KeyLexiconPrefix, DslSymbolKind.KeyLexicon),
        (UsharpLanguage.AnswerShapePrefix, DslSymbolKind.AnswerShape),
        (UsharpLanguage.WindowShapePrefix, DslSymbolKind.WindowShape),
        (UsharpLanguage.KeyPolicyPrefix, DslSymbolKind.KeyPolicy),
        (UsharpLanguage.KeyBridgePrefix, DslSymbolKind.KeyBridge),
    };

    /// <summary>默认 facet provider 集（序 = <see cref="Units"/>）。</summary>
    public static readonly IReadOnlyList<IUsharpFacetProvider> Providers = new IUsharpFacetProvider[]
    {
        new SpaceFacetProvider(),
        new ObsFacetProvider(),
        new UsharpFacetProvider<UsharpMatrixDecl>(DslSymbolKind.Matrix,
            ctx => ctx.Documents.SelectMany(UsharpSyntaxProjector.Matrices)),
        new DecisionFacetProvider(),
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
        new UsharpFacetProvider<UsharpReadModelDecl>(DslSymbolKind.ReadModel,
            ctx => ctx.Documents.SelectMany(UsharpSyntaxProjector.ReadModels)),
        new UsharpFacetProvider<UsharpRelationDecl>(DslSymbolKind.Relation,
            ctx => ctx.Documents.SelectMany(UsharpSyntaxProjector.Relations)),
        new UsharpFacetProvider<UsharpInvariantDecl>(DslSymbolKind.Invariant,
            ctx => ctx.Documents.SelectMany(UsharpSyntaxProjector.Invariants)),
        new UsharpFacetProvider<UsharpDeriveDecl>(DslSymbolKind.Derive,
            ctx => ctx.Documents.SelectMany(UsharpSyntaxProjector.Derivations)),
        new UsharpFacetProvider<UsharpMountDecl>(DslSymbolKind.Mount,
            ctx => ctx.Documents.SelectMany(UsharpSyntaxProjector.Mounts)),
        new UsharpFacetProvider<UsharpSegmentDecl>(DslSymbolKind.Segment,
            ctx => ctx.Declarations.OfKind(DslSymbolKind.Segment).Select(UsharpSegmentDecl.From).OfType<UsharpSegmentDecl>()),
        new UsharpFacetProvider<UsharpEntityDecl>(DslSymbolKind.Entity,
            ctx => ctx.Declarations.OfKind(DslSymbolKind.Entity).Select(UsharpEntityDecl.From).OfType<UsharpEntityDecl>()),
        new UsharpFacetProvider<UsharpKeyLexiconDecl>(DslSymbolKind.KeyLexicon,
            ctx => ctx.Declarations.OfKind(DslSymbolKind.KeyLexicon).Select(UsharpKeyLexiconDecl.From).OfType<UsharpKeyLexiconDecl>()),
        new UsharpFacetProvider<UsharpKeyShapeDecl>(DslSymbolKind.AnswerShape,
            ctx => ctx.Declarations.OfKind(DslSymbolKind.AnswerShape).Select(d => UsharpKeyShapeDecl.From(d, "Answer")).OfType<UsharpKeyShapeDecl>()
                .Concat(ctx.Declarations.OfKind(DslSymbolKind.WindowShape).Select(d => UsharpKeyShapeDecl.From(d, "Window")).OfType<UsharpKeyShapeDecl>())),
        new UsharpFacetProvider<UsharpIdentityPolicyDecl>(DslSymbolKind.KeyPolicy,
            ctx => ctx.Declarations.OfKind(DslSymbolKind.KeyPolicy).Select(UsharpIdentityPolicyDecl.From).OfType<UsharpIdentityPolicyDecl>()),
        new UsharpFacetProvider<UsharpKeyBridgeDecl>(DslSymbolKind.KeyBridge,
            ctx => ctx.Declarations.OfKind(DslSymbolKind.KeyBridge).Select(UsharpKeyBridgeDecl.Project).OfType<UsharpKeyBridgeDecl>()),
    };
}
