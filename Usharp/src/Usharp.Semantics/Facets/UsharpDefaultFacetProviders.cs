using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// Usharp 语言面默认 facet provider 集（H1/H2/H5 消除）：把 11 个领域视图从「核心构造器/编译方法体」
/// 平移到「可注册 provider」，语义逐字不变（薄包装 <see cref="UsharpSyntaxProjector"/>）。
/// <para>provider 序 = 既有编译流顺序（spaces → obs → 其余），保诊断归因序。</para>
/// </summary>
internal sealed class SpaceFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(SpaceRegistry);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Space };

    public object Project(UsharpFacetContext ctx)
    {
        var registry = new SpaceRegistry();
        foreach (var doc in ctx.Documents)
            foreach (var s in UsharpSyntaxProjector.Spaces(doc))
                registry.Add(s);
        return registry;
    }
}

/// <summary><c>[Obs]</c> 观测声明注册中心 facet（自带诊断）。</summary>
internal sealed class ObsFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(ObsRegistry);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Obs };

    public object Project(UsharpFacetContext ctx)
    {
        var registry = new ObsRegistry();
        foreach (var doc in ctx.Documents)
            foreach (var o in UsharpSyntaxProjector.Obs(doc))
                registry.Add(o);
        return registry;
    }
}

/// <summary><c>[Matrix]</c> 矩阵声明集。</summary>
internal sealed class MatrixFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(IReadOnlyList<UsharpMatrixDecl>);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Matrix };

    public object Project(UsharpFacetContext ctx)
        => ctx.Documents.SelectMany(UsharpSyntaxProjector.Matrices).ToList();
}

/// <summary><c>[Flow]</c> 流声明集。</summary>
internal sealed class FlowFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(IReadOnlyList<UsharpFlowDecl>);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Flow };

    public object Project(UsharpFacetContext ctx)
        => ctx.Documents.SelectMany(UsharpSyntaxProjector.Flows).ToList();
}

/// <summary><c>[Ref]</c> 引用声明集。</summary>
internal sealed class RefFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(IReadOnlyList<UsharpRefDecl>);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Ref };

    public object Project(UsharpFacetContext ctx)
        => ctx.Documents.SelectMany(UsharpSyntaxProjector.Refs).ToList();
}

/// <summary><c>[Domain]</c> 域定义声明集。</summary>
internal sealed class DomainFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(IReadOnlyList<UsharpDomainDecl>);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Domain };

    public object Project(UsharpFacetContext ctx)
        => ctx.Documents.SelectMany(UsharpSyntaxProjector.Domains).ToList();
}

/// <summary><c>[Gather]</c> 聚拢声明集。</summary>
internal sealed class GatherFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(IReadOnlyList<UsharpGatherDecl>);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Gather };

    public object Project(UsharpFacetContext ctx)
        => ctx.Documents.SelectMany(UsharpSyntaxProjector.Gathers).ToList();
}

/// <summary><c>[Table]</c> 表结构声明集。</summary>
internal sealed class TableFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(IReadOnlyList<UsharpTableDecl>);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Table };

    public object Project(UsharpFacetContext ctx)
        => ctx.Documents.SelectMany(UsharpSyntaxProjector.Tables).ToList();
}

/// <summary><c>[Record]</c> 记录载荷声明集。</summary>
internal sealed class RecordFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(IReadOnlyList<UsharpRecordDecl>);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Record };

    public object Project(UsharpFacetContext ctx)
        => ctx.Documents.SelectMany(UsharpSyntaxProjector.Records).ToList();
}

/// <summary><c>[Config]</c> 配置声明集。</summary>
internal sealed class ConfigFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(IReadOnlyList<UsharpConfigDecl>);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Config };

    public object Project(UsharpFacetContext ctx)
        => ctx.Documents.SelectMany(UsharpSyntaxProjector.Configs).ToList();
}

/// <summary><c>[Rule]</c> 规则声明集。</summary>
internal sealed class RuleFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(IReadOnlyList<UsharpRuleDecl>);
    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Rule };

    public object Project(UsharpFacetContext ctx)
        => ctx.Documents.SelectMany(UsharpSyntaxProjector.Rules).ToList();
}
