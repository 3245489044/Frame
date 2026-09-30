namespace Share.CircuitFramework.Compiler;

/// <summary>电路编译失败：图非法，不进入执行（非法图直接编译失败）。</summary>
public sealed class CircuitCompilationException : Exception
{
    public IReadOnlyList<CircuitDiagnostic> Diagnostics { get; }

    public CircuitCompilationException(IReadOnlyList<CircuitDiagnostic> diagnostics)
        : base($"电路编译失败：{diagnostics.Where(d => d.Severity == Compiler.DiagnosticSeverity.Error).Count()} 个错误。")
    {
        Diagnostics = diagnostics;
    }
}