using Plate.DslMethod;
using Plate.DslMethod.Logic;
using Plate.DslMethod.Model;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 通用 facet 投影器：把「泛型声明事实」投影为某个领域视图（可注册、可组合、零核心改动）。
/// <para>
/// 内核编译流不再枚举任何领域视图（H1/H2/H5）：一切视图由 provider 惰性投影，注册进
/// <see cref="UsharpFacetRegistry"/>，消费方经 <see cref="UsharpCompilation.Facet{T}"/> 读取。
/// </para>
/// </summary>
public interface IUsharpFacetProvider
{
    /// <summary>视图类型（如 <c>SpaceRegistry</c> / <c>IReadOnlyList&lt;UsharpFlowDecl&gt;</c>）。</summary>
    Type FacetType { get; }

    /// <summary>消费的声明类（facet 依赖的 <see cref="DslSymbolKind"/>；供依赖分析用）。</summary>
    IReadOnlyList<DslSymbolKind> Kinds { get; }

    /// <summary>纯投影，无副作用。</summary>
    object Project(UsharpFacetContext ctx);
}

/// <summary>facet 投影上下文（泛型声明事实的最小输入；不含任何领域视图）。</summary>
public sealed record UsharpFacetContext(
    IReadOnlyList<DslDocument> Documents,
    UsharpDeclarationIndex Declarations,
    DslIndex Index);

/// <summary>
/// 可选：provider 产物自带诊断（「注册中心型」facet，如 <see cref="SpaceRegistry"/> / <see cref="ObsRegistry"/>）。
/// 诊断按 provider 注册序汇集（保原有 <c>spaces → obs</c> 归因序）。
/// </summary>
public interface IUsharpDiagnosticCarrier
{
    IReadOnlyList<UsharpDiagnostic> Diagnostics { get; }
}
