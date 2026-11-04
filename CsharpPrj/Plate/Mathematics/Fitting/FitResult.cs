namespace MathematicalAlgorithms.Fitting;

/// <summary>
/// 拟合结果：成功必须同时满足 <see cref="Status"/> 为 <see cref="FitStatus.Ok"/> 且曲线非空。
/// <para><see cref="ObservedXMin"/> / <see cref="ObservedXMax"/> 只报告观测域，供上层构造公式域；
/// 拟合器本身不判断越域、不夹取。</para>
/// </summary>
public sealed record FitResult(
    FitStatus Status,
    IFitCurve? Curve,
    FitResiduals? Residuals,
    int SampleCount,
    double ObservedXMin,
    double ObservedXMax,
    string? Detail)
{
    public bool Success => Status == FitStatus.Ok && Curve is not null;

    public static FitResult Fail(
        FitStatus status,
        string detail,
        int sampleCount,
        double observedXMin = double.NaN,
        double observedXMax = double.NaN)
        => new(status, null, null, sampleCount, observedXMin, observedXMax, detail);
}
