namespace MathematicalAlgorithms.Fitting;

/// <summary>
/// 单参数标定：给定已知基函数 φ(x)，只拟合一个比例系数 <c>y = a*φ(x)</c>。
/// <para>用于「解析式为主、只标定一个系数」的场景；仍复用同一套最小二乘与残差计算。</para>
/// </summary>
public static class CalibrationFit
{
    public static FitResult Fit(
        IEnumerable<FitSample> samples,
        Func<double, double> basis,
        string coefficientName = "a",
        string expression = "a*phi(x)",
        FitOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(basis);

        if (string.IsNullOrWhiteSpace(coefficientName))
            return FitResult.Fail(FitStatus.InvalidInput, "系数名不能为空。", 0);

        return BasisFit.Fit(
            samples,
            new[] { basis },
            new[] { coefficientName },
            FitKind.Calibration,
            expression,
            options);
    }
}
