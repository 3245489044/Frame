namespace MathematicalAlgorithms.Fitting;

/// <summary>
/// 拟合残差：RMSE、最大绝对误差、最大相对误差、对数域 RMSE。
/// <para><see cref="MaxRelError"/> 在观测值为 0 而预测非零时返回 <see cref="double.PositiveInfinity"/>，
/// 显式暴露「相对误差无定义」，不做夹取。</para>
/// </summary>
public readonly record struct FitResiduals(
    double Rmse,
    double MaxAbsError,
    double MaxRelError,
    double? RmseLog)
{
    /// <summary>按样本与曲线计算残差。</summary>
    public static FitResiduals Compute(
        IReadOnlyList<FitSample> samples,
        Func<double, double> evaluate,
        double epsilon = 1e-12)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(evaluate);

        if (samples.Count == 0)
            return new(double.NaN, double.NaN, double.NaN, null);

        double sumSquares = 0;
        double maxAbs = 0;
        double maxRel = 0;
        double sumLogSquares = 0;
        int logCount = 0;

        for (int i = 0; i < samples.Count; i++)
        {
            FitSample sample = samples[i];
            double predicted = evaluate(sample.X);

            if (!double.IsFinite(predicted))
                return new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, null);

            double diff = predicted - sample.Y;
            double abs = Math.Abs(diff);
            sumSquares += diff * diff;
            if (abs > maxAbs) maxAbs = abs;

            double denominator = Math.Max(Math.Abs(sample.Y), epsilon);
            double relative = abs / denominator;
            if (Math.Abs(sample.Y) <= epsilon && abs > epsilon)
                relative = double.PositiveInfinity;
            if (relative > maxRel) maxRel = relative;

            if (sample.Y > 0 && predicted > 0)
            {
                double logDiff = Math.Log(predicted) - Math.Log(sample.Y);
                sumLogSquares += logDiff * logDiff;
                logCount++;
            }
        }

        double rmse = Math.Sqrt(sumSquares / samples.Count);
        double? rmseLog = logCount > 0 ? Math.Sqrt(sumLogSquares / logCount) : null;
        return new(rmse, maxAbs, maxRel, rmseLog);
    }
}
