namespace MathematicalAlgorithms.Fitting;

/// <summary>
/// 通用基函数拟合：<c>y = Σ cᵢ·φᵢ(x)</c>。
/// <para>线性、三次偏移都复用本入口，避免多套 OLS；基函数由调用方声明，模块不含领域语义。</para>
/// </summary>
public static class BasisFit
{
    public static FitResult Fit(
        IEnumerable<FitSample> samples,
        IReadOnlyList<Func<double, double>> basis,
        IReadOnlyList<string> coefficientNames,
        FitKind kind,
        string expression,
        FitOptions? options = null)
    {
        FitOptions fitOptions = options ?? FitOptions.Default;

        if (!fitOptions.IsValid)
            return FitResult.Fail(FitStatus.InvalidInput, "拟合参数非法。", 0);

        if (!FitSampleSet.TryCreate(samples, out FitSampleSet? set, out string? error))
            return FitResult.Fail(FitStatus.InvalidInput, error ?? "样本集合非法。", 0);

        FitSampleSet sampleSet = set!;
        if (basis is null || basis.Count == 0
            || coefficientNames is null
            || coefficientNames.Count != basis.Count)
        {
            return FitResult.Fail(
                FitStatus.InvalidInput,
                "基函数或系数名缺失/数量不一致。",
                sampleSet.Count,
                sampleSet.MinX,
                sampleSet.MaxX);
        }

        if (sampleSet.Count < fitOptions.MinimumSamples)
        {
            return FitResult.Fail(
                FitStatus.InsufficientSamples,
                $"样本数 {sampleSet.Count} 小于下限 {fitOptions.MinimumSamples}。",
                sampleSet.Count,
                sampleSet.MinX,
                sampleSet.MaxX);
        }

        var rows = new double[sampleSet.Count][];
        for (int i = 0; i < sampleSet.Count; i++)
        {
            var row = new double[basis.Count];
            for (int j = 0; j < basis.Count; j++)
            {
                double value = basis[j](sampleSet.Samples[i].X);
                if (!double.IsFinite(value))
                {
                    return FitResult.Fail(
                        FitStatus.InvalidInput,
                        "基函数产生非有限值。",
                        sampleSet.Count,
                        sampleSet.MinX,
                        sampleSet.MaxX);
                }

                row[j] = value;
            }

            rows[i] = row;
        }

        var observations = new double[sampleSet.Count];
        for (int i = 0; i < sampleSet.Count; i++)
            observations[i] = sampleSet.Samples[i].Y;

        if (!LinearLeastSquares.TrySolve(rows, observations, out double[] coefficients))
        {
            return FitResult.Fail(
                FitStatus.Degenerate,
                "设计矩阵秩不足或解非有限。",
                sampleSet.Count,
                sampleSet.MinX,
                sampleSet.MaxX);
        }

        var curve = new BasisCurve(kind, expression, basis, coefficientNames, coefficients);
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

    private sealed class BasisCurve : IFitCurve
    {
        private readonly IReadOnlyList<Func<double, double>> _basis;
        private readonly double[] _coefficients;
        private readonly FitCoefficient[] _exposed;

        public BasisCurve(
            FitKind kind,
            string expression,
            IReadOnlyList<Func<double, double>> basis,
            IReadOnlyList<string> names,
            double[] coefficients)
        {
            Kind = kind;
            Expression = expression;
            _basis = basis;
            _coefficients = coefficients;

            _exposed = new FitCoefficient[coefficients.Length];
            for (int i = 0; i < coefficients.Length; i++)
                _exposed[i] = new FitCoefficient(names[i], coefficients[i]);
        }

        public FitKind Kind { get; }

        public string Expression { get; }

        public IReadOnlyList<FitCoefficient> Coefficients => _exposed;

        public double Evaluate(double x)
        {
            double sum = 0;
            for (int i = 0; i < _basis.Count; i++)
                sum += _coefficients[i] * _basis[i](x);

            return sum;
        }
    }
}
