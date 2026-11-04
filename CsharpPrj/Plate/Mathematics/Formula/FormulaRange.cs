namespace MathematicalAlgorithms.Formula;

/// <summary>
/// 单个输入的适用域：闭区间与/或离散枚举值。
/// <para>枚举优先于区间；<see cref="Contains"/> 对 NaN/∞ 一律返回 <c>false</c>。</para>
/// </summary>
public sealed record FormulaRange(double? Min = null, double? Max = null, IReadOnlyList<double>? Values = null)
{
    public static FormulaRange Any { get; } = new();

    public static FormulaRange Between(double min, double max) => new(min, max, null);

    public static FormulaRange Exactly(params double[] values) => new(null, null, values);

    public bool Contains(double value)
    {
        if (!double.IsFinite(value))
            return false;

        if (Values is not null)
        {
            foreach (double candidate in Values)
            {
                if (candidate.Equals(value))
                    return true;
            }

            return false;
        }

        if (Min.HasValue && value < Min.Value)
            return false;

        if (Max.HasValue && value > Max.Value)
            return false;

        return true;
    }
}
