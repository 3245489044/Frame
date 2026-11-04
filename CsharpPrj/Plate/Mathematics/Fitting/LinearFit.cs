namespace MathematicalAlgorithms.Fitting;

/// <summary>线性拟合 <c>y = a*x + b</c>。</summary>
public static class LinearFit
{
    public static FitResult Fit(IEnumerable<FitSample> samples, FitOptions? options = null)
        => BasisFit.Fit(
            samples,
            new Func<double, double>[] { static x => x, static _ => 1.0 },
            new[] { "a", "b" },
            FitKind.Linear,
            "a*x + b",
            options);
}
