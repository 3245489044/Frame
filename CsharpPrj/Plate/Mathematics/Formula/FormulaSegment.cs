namespace MathematicalAlgorithms.Formula;

/// <summary>分段线性的一段：闭区间 [MinX, MaxX] 上的 <c>Slope*x + Intercept</c>。</summary>
public sealed record FormulaSegment(double MinX, double MaxX, double Slope, double Intercept);
