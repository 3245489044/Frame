namespace Usharp.Semantics;

/// <summary>
/// 诊断判据**单源**：error 判定（<c>Severity == Error</c>）与报表化前置筛选。
/// <para>
/// 消费方：编译门面（<see cref="UsharpCompilation"/>）/ 各发射产物（<see cref="UsharpDiagnosticResult"/>）/
/// 装载骨架（<c>UsharpDeclarationLoader</c>）——禁止各处再手写 <c>Any(d =&gt; d.Severity == Error)</c>（J-类判据收敛点）。
/// </para>
/// </summary>
public static class UsharpDiagnostics
{
    /// <summary>单条诊断是否为 error。</summary>
    /// <param name="diagnostic">诊断。</param>
    public static bool IsError(UsharpDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return diagnostic.Severity == UsharpSeverity.Error;
    }

    /// <summary>诊断集是否含 error（空集 ⇒ false）。</summary>
    /// <param name="diagnostics">诊断集。</param>
    public static bool HasError(IReadOnlyList<UsharpDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return diagnostics.Any(IsError);
    }

    /// <summary>筛出 error 诊断（声明序）。</summary>
    /// <param name="diagnostics">诊断集。</param>
    public static IReadOnlyList<UsharpDiagnostic> Errors(IReadOnlyList<UsharpDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return diagnostics.Where(IsError).ToList();
    }

    /// <summary>筛出警告诊断（声明序）。</summary>
    /// <param name="diagnostics">诊断集。</param>
    public static IReadOnlyList<UsharpDiagnostic> Warnings(IReadOnlyList<UsharpDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return diagnostics.Where(d => d.Severity == UsharpSeverity.Warning).ToList();
    }
}
