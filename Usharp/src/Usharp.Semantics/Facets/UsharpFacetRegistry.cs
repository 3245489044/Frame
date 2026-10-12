using System;
using System.Collections.Generic;

namespace Usharp.Semantics;

/// <summary>
/// facet 注册表（编译产物内层）：<see cref="Type"/> → 领域视图实例。
/// <para>
/// 由 <see cref="UsharpCompilation.CompileSources(System.Collections.Generic.IReadOnlyList{UsharpSourceFile},System.Collections.Generic.IReadOnlyList{IUsharpDomainPack})"/>
/// 在 facet 投影阶段填充（序 = pack 声明序 × provider 序）；诊断按注册序汇集（保归因序）。
/// </para>
/// </summary>
public sealed class UsharpFacetRegistry
{
    private readonly Dictionary<Type, object> _facets = new();
    private readonly List<UsharpDiagnostic> _diagnostics = new();

    /// <summary>投影期汇集诊断（provider 注册序；与既有 <c>spaces → obs</c> 序一致）。</summary>
    public IReadOnlyList<UsharpDiagnostic> Diagnostics => _diagnostics;

    /// <summary>登记一个 facet（重复类型 = 后注册覆盖，序以首次登记位置为准）。</summary>
    public void Register(Type facetType, object facet)
    {
        ArgumentNullException.ThrowIfNull(facetType);
        ArgumentNullException.ThrowIfNull(facet);
        _facets[facetType] = facet;
    }

    /// <summary>汇集诊断（provider 产物实现 <see cref="IUsharpDiagnosticCarrier"/> 时）。</summary>
    public void AddDiagnostics(IEnumerable<UsharpDiagnostic> diagnostics)
    {
        if (diagnostics is null) return;
        _diagnostics.AddRange(diagnostics);
    }

    /// <summary>取 facet（未注册 → false）。</summary>
    public bool TryGetFacet<T>(out T facet) where T : class
    {
        if (_facets.TryGetValue(typeof(T), out var value) && value is T typed)
        {
            facet = typed;
            return true;
        }

        facet = null!;
        return false;
    }

    /// <summary>取 facet（未注册 → 抛，不静默给空；对齐「未声明即失败」纪律）。</summary>
    public T Facet<T>() where T : class
        => TryGetFacet<T>(out var facet)
            ? facet
            : throw new InvalidOperationException($"facet 未注册：{typeof(T).FullName}");
}
