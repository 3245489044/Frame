namespace MathematicalAlgorithms.Formula;

/// <summary>拟合证据来源：文件名、SHA-256、区段。</summary>
public sealed record FormulaSource(string File, string Sha256, string Section);
