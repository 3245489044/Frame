namespace Usharp.Semantics;

/// <summary>facet 读取面（<see cref="UsharpCompilation"/> 与 <see cref="UsharpGateModel"/> 共用形状）。</summary>
public interface IUsharpFacetReader
{
    /// <summary>取 facet 视图（未注册 → 抛，不静默给空）。</summary>
    T Facet<T>() where T : class;

    /// <summary>取 facet 视图（未注册 → false）。</summary>
    bool TryGetFacet<T>(out T facet) where T : class;
}
