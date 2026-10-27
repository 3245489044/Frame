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

