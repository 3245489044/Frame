namespace MathematicalAlgorithms.Fitting;

/// <summary>
/// 幂律拟合 <c>y = a*x^b</c>（log-log 最小二乘）。
/// <para>要求全部 <c>x&gt;0 ∧ y&gt;0</c>；不提供自动偏移或夹到 ε，避免掩盖数据问题。</para>
/// </summary>
public static class PowerLawFit
{
    public static FitResult Fit(IEnumerable<FitSample> samples, FitOptions? options = null)
    {
        FitOptions fitOptions = options ?? FitOptions.Default;
        if (!fitOptions.IsValid)
            return FitResult.Fail(FitStatus.InvalidInput, "拟合参数非法。", 0);

        if (!FitSampleSet.TryCreate(samples, out FitSampleSet? set, out string? error))
            return FitResult.Fail(FitStatus.InvalidInput, error ?? "样本集合非法。", 0);

        FitSampleSet sampleSet = set!;
        if (sampleSet.Count < fitOptions.MinimumSamples)
        {
            return FitResult.Fail(
                FitStatus.InsufficientSamples,
                $"样本数 {sampleSet.Count} 小于下限 {fitOptions.MinimumSamples}。",
                sampleSet.Count,
                sampleSet.MinX,
                sampleSet.MaxX);
        }

        for (int i = 0; i < sampleSet.Count; i++)
        {
            FitSample sample = sampleSet.Samples[i];
            if (sample.X <= 0 || sample.Y <= 0)
            {
                return FitResult.Fail(
                    FitStatus.InvalidInput,
                    "幂律拟合要求全部样本 X>0 且 Y>0。",
                    sampleSet.Count,
                    sampleSet.MinX,
                    sampleSet.MaxX);
            }
        }

        var rows = new double[sampleSet.Count][];
        var observations = new double[sampleSet.Count];
        for (int i = 0; i < sampleSet.Count; i++)
        {
            FitSample sample = sampleSet.Samples[i];
            rows[i] = new[] { Math.Log(sample.X), 1.0 };
            observations[i] = Math.Log(sample.Y);
        }

        if (!LinearLeastSquares.TrySolve(rows, observations, out double[] coefficients))
        {
            return FitResult.Fail(
                FitStatus.Degenerate,
                "log-log 设计矩阵秩不足或解非有限。",
                sampleSet.Count,
                sampleSet.MinX,
                sampleSet.MaxX);
        }

        double exponent = coefficients[0];
        double scale = Math.Exp(coefficients[1]);
        if (!double.IsFinite(exponent) || !double.IsFinite(scale) || scale <= 0)
        {
            return FitResult.Fail(
                FitStatus.NonConverged,
                "幂律系数非有限。",
                sampleSet.Count,
                sampleSet.MinX,
                sampleSet.MaxX);
        }

        var curve = new PowerLawCurve(scale, exponent);
        FitResiduals residuals = FitResiduals.Compute(sampleSet.Samples, curve.Evaluate, fitOptions.Epsilon);
        if (!double.IsFinite(residuals.Rmse))
        {
            return FitResult.Fail(
                FitStatus.NonConverged,
                "拟合残差非有限。",
                sampleSet.Count,
                sampleSet.MinX,
                sampleSet.MaxX);
        }

        return new FitResult(
            FitStatus.Ok,
            curve,
            residuals,
            sampleSet.Count,
            sampleSet.MinX,
            sampleSet.MaxX,
            null);
    }

    private sealed class PowerLawCurve : IFitCurve
    {
        private readonly double _scale;
        private readonly double _exponent;
        private readonly FitCoefficient[] _coefficients;

        public PowerLawCurve(double scale, double exponent)
        {
            _scale = scale;
            _exponent = exponent;
            _coefficients = new[]
            {
                new FitCoefficient("a", scale),
                new FitCoefficient("b", exponent),
            };
        }

        public FitKind Kind => FitKind.PowerLaw;

        public string Expression => "a*x^b";

        public IReadOnlyList<FitCoefficient> Coefficients => _coefficients;

        public double Evaluate(double x) => _scale * Math.Pow(x, _exponent);
    }
}
