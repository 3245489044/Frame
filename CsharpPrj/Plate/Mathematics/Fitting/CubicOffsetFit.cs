namespace MathematicalAlgorithms.Fitting;

/// <summary>三次偏移拟合 <c>y = a*x^3 + b</c>，对应「单块耗时随块体积」这类目标式。</summary>
public static class CubicOffsetFit
{
    public static FitResult Fit(IEnumerable<FitSample> samples, FitOptions? options = null)
        => BasisFit.Fit(
            samples,
            new Func<double, double>[] { static x => x * x * x, static _ => 1.0 },
            new[] { "a", "b" },
            FitKind.CubicOffset,
            "a*x^3 + b",
            options);
}
