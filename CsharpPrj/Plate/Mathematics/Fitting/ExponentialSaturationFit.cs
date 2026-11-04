namespace MathematicalAlgorithms.Fitting;

/// <summary>
/// 指数饱和拟合 <c>y = a*(1 - exp(-b*x)) + c</c>。
/// <para>固定增长率 b 做确定性对数网格，再对 a、c 做线性最小二乘；网格最优点邻域用固定次数三分法细化。
/// 并列残差取更小的 b，保证同一输入逐位可复现。</para>
/// </summary>
public static class ExponentialSaturationFit
{
    private const int GridSteps = 128;
    private const int RefinementIterations = 64;
    private const double MinRateRatio = 1e-3;
    private const double MaxRateRatio = 1e3;

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

        if (sampleSet.MinX < 0)
        {
            return FitResult.Fail(
                FitStatus.InvalidInput,
                "指数饱和拟合要求 X 非负。",
                sampleSet.Count,
                sampleSet.MinX,
                sampleSet.MaxX);
        }

        if (sampleSet.MaxX <= 0)
        {
            return FitResult.Fail(
                FitStatus.InvalidInput,
                "指数饱和拟合要求 X 的最大值为正。",
                sampleSet.Count,
                sampleSet.MinX,
                sampleSet.MaxX);
        }

        if (!sampleSet.HasDistinctX)
        {
            return FitResult.Fail(
                FitStatus.Degenerate,
                "全部样本 X 相同，增长率参数不可辨识。",
                sampleSet.Count,
                sampleSet.MinX,
                sampleSet.MaxX);
        }

        double bMin = Math.Max(MinRateRatio / sampleSet.MaxX, fitOptions.Epsilon);
        double bMax = Math.Max(MaxRateRatio / sampleSet.MaxX, bMin * 10);
        double logMin = Math.Log(bMin);
        double logMax = Math.Log(bMax);

        var candidates = new double[GridSteps];
        for (int i = 0; i < GridSteps; i++)
        {
            double t = (double)i / (GridSteps - 1);
            candidates[i] = Math.Exp(logMin + (logMax - logMin) * t);
        }

        bool hasBest = false;
        double bestB = 0;
        double bestA = 0;
        double bestC = 0;
        double bestRmse = double.PositiveInfinity;
        int bestIndex = -1;

        for (int i = 0; i < GridSteps; i++)
        {
            if (!TryFitForRate(sampleSet, candidates[i], fitOptions.Epsilon, out double a, out double c, out double rmse))
                continue;

            if (IsBetter(candidates[i], rmse, hasBest, bestB, bestRmse))
            {
                hasBest = true;
                bestB = candidates[i];
                bestA = a;
                bestC = c;
                bestRmse = rmse;
                bestIndex = i;
            }
        }

        if (!hasBest)
        {
            return FitResult.Fail(
                FitStatus.NonConverged,
                "指数饱和网格未得到有限残差。",
                sampleSet.Count,
                sampleSet.MinX,
                sampleSet.MaxX);
        }

        double lower = bestIndex > 0 ? candidates[bestIndex - 1] : bMin;
        double upper = bestIndex < GridSteps - 1 ? candidates[bestIndex + 1] : bMax;
        if (!(upper > lower))
        {
            lower = Math.Max(bMin, bestB * 0.5);
            upper = Math.Min(bMax, bestB * 2.0);
            if (!(upper > lower))
            {
                lower = bMin;
                upper = bMax;
            }
        }

        for (int iteration = 0; iteration < RefinementIterations; iteration++)
        {
            if (!(upper > lower))
                break;

            double third = (upper - lower) / 3.0;
            double left = lower + third;
            double right = upper - third;

            bool leftOk = TryFitForRate(sampleSet, left, fitOptions.Epsilon, out double leftA, out double leftC, out double leftRmse);
            bool rightOk = TryFitForRate(sampleSet, right, fitOptions.Epsilon, out double rightA, out double rightC, out double rightRmse);

            if (leftOk && IsBetter(left, leftRmse, hasBest, bestB, bestRmse))
            {
                bestB = left;
                bestA = leftA;
                bestC = leftC;
                bestRmse = leftRmse;
            }

            if (rightOk && IsBetter(right, rightRmse, hasBest, bestB, bestRmse))
            {
                bestB = right;
                bestA = rightA;
                bestC = rightC;
                bestRmse = rightRmse;
            }

            if (leftOk && (!rightOk || leftRmse <= rightRmse))
                upper = right;
            else
                lower = left;
        }

        var curve = new ExponentialSaturationCurve(bestA, bestB, bestC);
        FitResiduals residuals = FitResiduals.Compute(sampleSet.Samples, curve.Evaluate, fitOptions.Epsilon);
        if (!double.IsFinite(residuals.Rmse))
        {
            return FitResult.Fail(
                FitStatus.NonConverged,
                "指数饱和拟合残差非有限。",
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

    private static bool IsBetter(double candidateB, double candidateRmse, bool hasBest, double bestB, double bestRmse)
    {
        if (!double.IsFinite(candidateRmse) || !(candidateB > 0))
            return false;

        if (!hasBest)
            return true;

        if (candidateRmse < bestRmse)
            return true;

        return candidateRmse == bestRmse && candidateB < bestB;
    }

    private static bool TryFitForRate(
        FitSampleSet sampleSet,
        double rate,
        double epsilon,
        out double a,
        out double c,
        out double rmse)
    {
        a = 0;
        c = 0;
        rmse = double.PositiveInfinity;

        if (!(rate > 0) || !double.IsFinite(rate))
            return false;

        int count = sampleSet.Count;
        var rows = new double[count][];
        var observations = new double[count];

        for (int i = 0; i < count; i++)
        {
            FitSample sample = sampleSet.Samples[i];
            double feature = 1.0 - Math.Exp(-rate * sample.X);
            if (!double.IsFinite(feature))
                return false;

            rows[i] = new[] { feature, 1.0 };
            observations[i] = sample.Y;
        }

        if (!LinearLeastSquares.TrySolve(rows, observations, out double[] coefficients))
            return false;

        a = coefficients[0];
        c = coefficients[1];
        if (!double.IsFinite(a) || !double.IsFinite(c))
            return false;

        double sumSquares = 0;
        for (int i = 0; i < count; i++)
        {
            FitSample sample = sampleSet.Samples[i];
            double predicted = a * (1.0 - Math.Exp(-rate * sample.X)) + c;
            if (!double.IsFinite(predicted))
                return false;

            double diff = predicted - sample.Y;
            sumSquares += diff * diff;
        }

        rmse = Math.Sqrt(sumSquares / count);
        return double.IsFinite(rmse);
    }

    private sealed class ExponentialSaturationCurve : IFitCurve
    {
        private readonly double _a;
        private readonly double _b;
        private readonly double _c;
        private readonly FitCoefficient[] _coefficients;

        public ExponentialSaturationCurve(double a, double b, double c)
        {
            _a = a;
            _b = b;
            _c = c;
            _coefficients = new[]
            {
                new FitCoefficient("a", a),
                new FitCoefficient("b", b),
                new FitCoefficient("c", c),
            };
        }

        public FitKind Kind => FitKind.ExponentialSaturation;

        public string Expression => "a*(1 - exp(-b*x)) + c";

        public IReadOnlyList<FitCoefficient> Coefficients => _coefficients;

        public double Evaluate(double x)
        {
            if (!(_b > 0))
                return double.NaN;

            return _a * (1.0 - Math.Exp(-_b * x)) + _c;
        }
    }
}
