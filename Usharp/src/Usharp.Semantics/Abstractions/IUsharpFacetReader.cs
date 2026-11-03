namespace Usharp.Semantics;

/// <summary>facet 读取面（<see cref="UsharpCompilation"/> 与 <see cref="UsharpGateModel"/> 共用形状）。</summary>
/// <remarks>
/// 本接口只定义**读取原语**；视图集（领域/核心视图）的单一实现在
/// <see cref="UsharpFacetViewSet"/>（D-5），禁止各读取面再写一套 <c>TryGetFacet ? x : Array.Empty</c>。
/// </remarks>
public interface IUsharpFacetReader
{
    /// <summary>取 facet 视图（未注册 → 抛，不静默给空）。</summary>
    T Facet<T>() where T : class;

    /// <summary>取 facet 视图（未注册 → false）。</summary>
    bool TryGetFacet<T>(out T facet) where T : class;
}
