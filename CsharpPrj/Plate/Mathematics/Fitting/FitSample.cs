namespace MathematicalAlgorithms.Fitting;

/// <summary>
/// 一元拟合样本点（X 为自变量，Y 为观测值）。
/// <para>只承载数值，不携带指标名、量纲或条件；领域语义由调用方映射。</para>
/// </summary>
public readonly record struct FitSample(double X, double Y)
{
    /// <summary>X/Y 均为有限值（NaN/∞ 不参与拟合）。</summary>
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}
