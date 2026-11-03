using Usharp.Semantics;

namespace Usharp.Emit;

/// <summary>一次投影器执行结果（产物 + 诊断）。</summary>
/// <param name="Id">投影器 id（如 <c>flow.roundspec</c>）。</param>
/// <param name="Value">产物（失败/未激活可为 null）。</param>
/// <param name="Diagnostics">执行期诊断。</param>
public sealed record UsharpProjection(
    string Id, object? Value, IReadOnlyList<UsharpDiagnostic> Diagnostics)
{
    /// <summary>是否含 error。</summary>
    public bool HasErrors => UsharpDiagnostics.HasError(Diagnostics);
}

/// <summary>
/// 发射编排内核（R-1/R-3 的收敛点）：统一执行 <see cref="IUsharpFacetProjector"/>，
/// 收集产物与诊断；投影器抛出的异常转为可见诊断，不再静默吞掉。
/// <para>消费方 = Bundle / Node / Agent 宿主；语义层（<c>UsharpCompilation</c>）不依赖产物类型。</para>
/// </summary>
public static class UsharpEmitPipeline
{
    /// <summary>执行全部投影器（序 = 传入序）。</summary>
    /// <param name="compilation">编译结果。</param>
    /// <param name="projectors">投影器集（通常来自 <c>IUsharpDomainPack.FacetProjectors</c>）。</param>
    public static IReadOnlyList<UsharpProjection> Run(
        UsharpCompilation compilation, IEnumerable<IUsharpFacetProjector> projectors)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(projectors);

        var results = new List<UsharpProjection>();
        foreach (var projector in projectors)
        {
            ArgumentNullException.ThrowIfNull(projector);

            object? value = null;
            var diagnostics = new List<UsharpDiagnostic>();
            try
            {
                value = projector.Emit(compilation);
            }
            catch (Exception ex)
            {
                diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.DeclarationLoadFailure,
                    $"投影器 '{projector.Id}' 执行异常：{ex.GetType().Name}: {ex.Message}",
                    "(emit)", 0));
            }

            if (value is IUsharpDiagnosticCarrier carrier)
                diagnostics.AddRange(carrier.Diagnostics);

            results.Add(new UsharpProjection(projector.Id, value, diagnostics));
        }

        return results;
    }
}
