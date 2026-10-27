using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod;

namespace Usharp.Semantics;

/// <summary>
/// 通用 facet provider：一个 <see cref="DslSymbolKind"/> + 一个投影函数 = 一个领域视图。
/// <para>
/// 吸收 21 处同构 provider（<c>FacetType = IReadOnlyList&lt;T&gt;</c> / 单 Kind / <c>SelectMany(Projector.X)</c>）；
/// 领域特有逻辑（注册中心 / 多 Kind 索引 / 条件抽取）仍保留专用 provider。
/// </para>
/// </summary>
public sealed class UsharpFacetProvider<TDecl> : IUsharpFacetProvider
    where TDecl : class
{
    private readonly DslSymbolKind _kind;
    private readonly Func<UsharpFacetContext, IEnumerable<TDecl>> _project;

    public UsharpFacetProvider(DslSymbolKind kind, Func<UsharpFacetContext, IEnumerable<TDecl>> project)
    {
        _kind = kind;
        _project = project ?? throw new ArgumentNullException(nameof(project));
    }

    public Type FacetType => typeof(IReadOnlyList<TDecl>);

    public IReadOnlyList<DslSymbolKind> Kinds => new[] { _kind };

    public object Project(UsharpFacetContext ctx) => _project(ctx).ToList();
}
