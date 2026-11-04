namespace MathematicalAlgorithms.Formula;

/// <summary>公式输入声明：名称、量纲、适用域。</summary>
public sealed record FormulaInputSpec(string Name, string Unit, FormulaRange Domain);
