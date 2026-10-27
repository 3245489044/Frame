using System.Collections.Generic;
using System.Linq;
using Usharp.Semantics;

namespace Usharp.Emit;

/// <summary>
/// 发射产物基类：吸收各 <c>*Result</c> 记录共有的两处样板（<c>DiagnosticList</c> / <c>EmptyReport</c>）。
/// <para>子类只保留自身载荷字段（单行壳），诊断出口不再逐份重写。</para>
/// </summary>
public abstract record UsharpEmitResultBase(
    IReadOnlyList<UsharpDiagnostic> Diagnostics) : UsharpDiagnosticResult
{
    /// <inheritdoc />
    protected override IReadOnlyList<UsharpDiagnostic> DiagnosticList => Diagnostics;

    /// <inheritdoc />
    protected override string EmptyReport => "(no emit diagnostics)";
}

/// <summary>单载荷发射产物记录（<c>Value</c> + 诊断；骨架构件的通用返回形态）。</summary>
public sealed record UsharpEmitResult<T>(
    T Value,
    IReadOnlyList<UsharpDiagnostic> Diagnostics) : UsharpEmitResultBase(Diagnostics);

/// <summary>发射骨架：按 id 找单条声明 → 缺失即报码不产出（归因取给定装载面）。</summary>
public static class UsharpEmit
{
    /// <summary>
    /// 统一「按 id 找单条 → 命中则投影；缺失 ⇒ 不产出 + 报 <paramref name="notProducibleCode"/>」。
    /// <para>诊断文案与归因面由调用方给定（各发射器的对外文案属既有契约，禁在此改写）。</para>
    /// </summary>
    public static UsharpEmitResult<TDoc> Document<TDecl, TDoc>(
        IEnumerable<TDecl> source,
        string id,
        System.Func<TDecl, string> idOf,
        string notProducibleCode,
        string message,
        string file,
        System.Func<TDecl, TDoc> project)
        where TDecl : class
        where TDoc : class
    {
        System.ArgumentNullException.ThrowIfNull(source);
        System.ArgumentNullException.ThrowIfNull(idOf);
        System.ArgumentNullException.ThrowIfNull(project);

        var decl = source.FirstOrDefault(d => string.Equals(idOf(d), id, System.StringComparison.Ordinal));
        if (decl is null)
        {
            return new UsharpEmitResult<TDoc>(null!, new[]
            {
                UsharpDiagnostic.Error(notProducibleCode, message, file, 0),
            });
        }

        return new UsharpEmitResult<TDoc>(project(decl), System.Array.Empty<UsharpDiagnostic>());
    }
}
