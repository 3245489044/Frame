namespace MathematicalAlgorithms.Fitting;

/// <summary>
/// 拟合参数。
/// <para><see cref="MinimumSamples"/> 默认 5，对应设计稿「点数 &lt; 5 禁止拟合」的硬规则；
/// 数学测试可显式覆盖，但生产调用方必须承担过拟合责任。</para>
/// </summary>
public sealed record FitOptions
{
    /// <summary>允许拟合的最小样本数（默认 5）。</summary>
    public int MinimumSamples { get; init; } = 5;

    /// <summary>数值零阈值，用于相对误差分母与分母接近零的判据。</summary>
    public double Epsilon { get; init; } = 1e-12;

    /// <summary>有理饱和拟合的 b 网格点数（升序、对数等距）。</summary>
    public int RationalGridSteps { get; init; } = 128;

    /// <summary>网格最优邻域内的固定细化迭代次数。</summary>
    public int RationalRefinementIterations { get; init; } = 64;

    /// <summary>b 下界相对 maxX 的比例。</summary>
    public double RationalMinDenominatorRatio { get; init; } = 1e-3;

    /// <summary>b 上界相对 maxX 的比例。</summary>
    public double RationalMaxDenominatorRatio { get; init; } = 1e3;

    /// <summary>默认参数实例。</summary>
    public static FitOptions Default { get; } = new();

    /// <summary>参数是否可用于拟合。</summary>
    public bool IsValid =>
        MinimumSamples >= 2
        && Epsilon > 0
        && double.IsFinite(Epsilon)
        && RationalGridSteps >= 2
        && RationalRefinementIterations >= 0
        && RationalMinDenominatorRatio > 0
        && RationalMaxDenominatorRatio > RationalMinDenominatorRatio;
}
