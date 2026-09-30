namespace Share.CircuitFramework.Compiler;

public enum DiagnosticSeverity { Error, Warning }

/// <summary>编译期诊断：图校验产物。错误级诊断会导致编译失败，不进入执行。</summary>
public sealed record CircuitDiagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    string? NodeId = null);