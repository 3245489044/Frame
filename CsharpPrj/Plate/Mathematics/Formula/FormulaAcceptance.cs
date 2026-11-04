using MathematicalAlgorithms.Fitting;

namespace MathematicalAlgorithms.Formula;

/// <summary>
/// 标定公式的验收判据（设计稿 §8.3）：点数下限、相对误差、对数域 RMSE。
/// <para>只用于 <see cref="FormulaKind.Calibrated"/>；常量/闭式公式不套用该阈值。</para>
/// </summary>
public static class FormulaAcceptance
{
    public const int MinimumCalibrationSamples = 5;
    public const double MaxRelativeError = 0.20;
    public const double MaxRmseLog = 0.25;

    public static bool IsCalibrationAcceptable(int samples, FitResiduals residual, bool hasSources)
        => samples >= MinimumCalibrationSamples
           && hasSources
           && double.IsFinite(residual.Rmse)
           && residual.MaxRelError <= MaxRelativeError
           && (!residual.RmseLog.HasValue || residual.RmseLog.Value <= MaxRmseLog);

    public static FormulaStatus DetermineStatus(int samples, FitResiduals residual, bool hasSources)
        => IsCalibrationAcceptable(samples, residual, hasSources)
            ? FormulaStatus.Active
            : FormulaStatus.Provisional;
}
