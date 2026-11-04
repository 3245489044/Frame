using MathematicalAlgorithms.Fitting;
using MathematicalAlgorithms.Formula;

namespace MathematicalAlgorithms.FormulaFit;

public static partial class FormulaFitRunner
{
    private static bool TryBuildDefinition(
        FormulaFitTargetPlan target,
        FormulaFitPlan plan,
        FitResult fit,
        IReadOnlyDictionary<string, string> conditions,
        FormulaSource[] sources,
        out FormulaDefinition? definition,
        out string? error)
    {
        definition = null;
        error = null;

        if (fit.Curve is null || fit.Residuals is null)
        {
            error = $"target {target.Id} 缺少曲线或残差。";
            return false;
        }

        FormulaInputSpec[] inputs = target.Inputs.Select(static input => input.ToSpec()).ToArray();
        var ranges = new SortedDictionary<string, FormulaRange>(StringComparer.Ordinal);
        foreach (FormulaInputSpec input in inputs)
            ranges[input.Name] = input.Domain;

        FitResiduals residual = fit.Residuals.Value;
        FormulaStatus status = FormulaAcceptance.DetermineStatus(
            fit.SampleCount,
            residual,
            sources.Length > 0);

        try
        {
            definition = new FormulaDefinition
            {
                Id = target.Id,
                Version = target.Version,
                Kind = FormulaKind.Calibrated,
                Shape = target.Shape,
                Target = target.Target,
                Unit = target.Unit,
                Inputs = inputs,
                Expression = fit.Curve.Expression,
                Coefficients = fit.Curve.Coefficients
                    .Select(static coefficient => new FormulaCoefficient(coefficient.Name, coefficient.Value))
                    .ToArray(),
                Fit = new FormulaFitRecord(
                    MethodOf(target.Shape),
                    new FormulaFitWindow(
                        plan.Window?.From ?? string.Empty,
                        plan.Window?.To ?? string.Empty,
                        conditions),
                    fit.SampleCount,
                    sources),
                Residual = residual,
                Domain = new FormulaDomain(ranges),
                Status = status,
            }.Normalize();
            return true;
        }
        catch (Exception ex)
        {
            error = $"target {target.Id} 公式定义校验失败：{ex.Message}";
            return false;
        }
    }

    private static bool TryValidateObservedDomain(
        FormulaFitTargetPlan target,
        IReadOnlyList<FitSample> samples,
        out string? error)
    {
        error = null;
        FormulaRange range = target.Inputs[0].ToRange();

        foreach (FitSample sample in samples)
        {
            if (!range.Contains(sample.X))
            {
                error = $"target {target.Id} 的观测 x={sample.X} 超出计划域。";
                return false;
            }
        }

        return true;
    }

    private static FitResult FitByShape(
        FormulaShape shape,
        IReadOnlyList<FitSample> samples,
        FitOptions options)
        => shape switch
        {
            FormulaShape.Linear => LinearFit.Fit(samples, options),
            FormulaShape.CubicOffset => CubicOffsetFit.Fit(samples, options),
            FormulaShape.PowerLaw => PowerLawFit.Fit(samples, options),
            FormulaShape.RationalSaturation => RationalSaturationFit.Fit(samples, options),
            _ => throw new InvalidOperationException($"离线拟合不支持形状 {shape}。"),
        };

    private static string MethodOf(FormulaShape shape)
        => shape switch
        {
            FormulaShape.Linear => "linear",
            FormulaShape.CubicOffset => "cubic-offset",
            FormulaShape.PowerLaw => "power-law",
            FormulaShape.RationalSaturation => "rational-saturation",
            _ => shape.ToString(),
        };
}
