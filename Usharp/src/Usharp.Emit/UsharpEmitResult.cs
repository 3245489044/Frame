using System.Collections.Generic;
using System.Linq;
using Usharp.Semantics;

namespace Usharp.Emit;

/// <summary>发射产物记录（吸收 7 份 Result 记录的基类样板：<c>DiagnosticList</c> / <c>EmptyReport</c>）。</summary>
public sealed record UsharpEmitResult<T>(
    T Value,
    IReadOnlyList<UsharpDiagnostic> Diagnostics) : UsharpDiagnosticResult
{
    /// <inheritdoc />
    protected override IReadOnlyList<UsharpDiagnostic> DiagnosticList => Diagnostics;

    /// <inheritdoc />
    protected override string EmptyReport => "(no emit diagnostics)";
}

/// <summary>发射骨架：按 id 找单条声明 → 不可产出即报码（归因取声明 <c>文件:行</c>）。</summary>
public static class UsharpEmit
{
    /// <summary>
    /// 统一「按 id 找单条 → 找不到 / 投影失败 ⇒ 不产出 + 报 <paramref name="notProducibleCode"/>」。
    /// </summary>
    public static UsharpEmitResult<TDoc> Document<TDecl, TDoc>(
        UsharpCompilation compilation,
        string id,
        string notProducibleCode,
        string label,
        IEnumerable<TDecl> source,
        System.Func<TDecl, string> idOf,
        System.Func<TDecl, (string File, int Line)> attribution,
        System.Func<TDecl, TDoc?> project)
        where TDecl : class
        where TDoc : class
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        System.ArgumentNullException.ThrowIfNull(source);

        var decl = source.FirstOrDefault(d => string.Equals(idOf(d), id, System.StringComparison.Ordinal));
        if (decl is null)
        {
            var file = compilation.Documents.FirstOrDefault()?.FilePath ?? "(request)";
            return new UsharpEmitResult<TDoc>(null!, new[]
            {
                UsharpDiagnostic.Error(notProducibleCode,
                    $"{label}'{id}' 不可产出：本编译未声明该 id", file, 0),
            });
        }

        var doc = project(decl);
        var (sourceFile, line) = attribution(decl);
        return doc is not null
            ? new UsharpEmitResult<TDoc>(doc, System.Array.Empty<UsharpDiagnostic>())
            : new UsharpEmitResult<TDoc>(null!, new[]
            {
                UsharpDiagnostic.Error(notProducibleCode, $"{label}'{id}' 不可产出：形态非法", sourceFile, line),
            });
    }
}
