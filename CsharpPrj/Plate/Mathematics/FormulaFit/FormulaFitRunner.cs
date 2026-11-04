using System.Globalization;
using MathematicalAlgorithms.Fitting;
using MathematicalAlgorithms.Formula;

namespace MathematicalAlgorithms.FormulaFit;

/// <summary>单个 target 的拟合报告。</summary>
public sealed record FormulaFitTargetReport(
    string Id,
    int Samples,
    FormulaStatus Status,
    double Rmse,
    double MaxAbsError,
    double MaxRelError,
    string? Detail);

/// <summary>一次离线拟合结果；任一 target 失败时不产出部分公式集。</summary>
public sealed record FormulaFitRunResult(
    bool Success,
    FormulaSet? FormulaSet,
    IReadOnlyList<FormulaFitTargetReport> Targets,
    IReadOnlyList<string> Errors);

public static partial class FormulaFitRunner
{
    public static FormulaFitRunResult Run(
        FormulaFitPlan plan,
        IReadOnlyList<SweepDocument> sweeps,
        DateTimeOffset generatedAt,
        TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sweeps);
        ArgumentNullException.ThrowIfNull(log);

        var definitions = new List<FormulaDefinition>();
        var reports = new List<FormulaFitTargetReport>();
        var errors = new List<string>();

        foreach (FormulaFitTargetPlan target in plan.Targets)
        {
            if (!FormulaFitSamples.TryCreate(
                    target,
                    sweeps,
                    out FitSample[] samples,
                    out IReadOnlyList<SweepPoint> usedPoints,
                    out IReadOnlySet<string> usedSourcePaths,
                    out string? sampleError))
            {
                errors.Add(sampleError ?? $"target {target.Id} 样本提取失败。");
                continue;
            }

            if (!TryValidateObservedDomain(target, samples, out string? domainError))
            {
                errors.Add(domainError!);
                continue;
            }

            var fitOptions = new FitOptions { MinimumSamples = target.MinimumSamples };
            FitResult fit = FitByShape(target.Shape, samples, fitOptions);
            if (!fit.Success || fit.Curve is null || fit.Residuals is null)
            {
                errors.Add($"target {target.Id} 拟合失败（{fit.Status}）：{fit.Detail}");
                continue;
            }

            if (!FormulaFitConditions.TryCollect(
                    plan,
                    target,
                    usedPoints,
                    out IReadOnlyDictionary<string, string> conditions,
                    out string? conditionError))
            {
                errors.Add(conditionError!);
                continue;
            }

            FormulaSource[] sources;
            try
            {
                sources = usedSourcePaths
                    .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                    .Select(path => new FormulaSource(
                        Path.GetFileName(path),
                        FormulaFitSamples.ComputeSha256(path),
                        target.Id))
                    .ToArray();
            }
            catch (Exception ex)
            {
                errors.Add($"target {target.Id} 读取证据来源失败：{ex.Message}");
                continue;
            }

            if (!TryBuildDefinition(target, plan, fit, conditions, sources, out FormulaDefinition? definition, out string? definitionError))
            {
                errors.Add(definitionError!);
                continue;
            }

            definitions.Add(definition!);
            FitResiduals residual = fit.Residuals.Value;
            reports.Add(new FormulaFitTargetReport(
                target.Id,
                fit.SampleCount,
                definition!.Status,
                residual.Rmse,
                residual.MaxAbsError,
                residual.MaxRelError,
                null));

            log.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "[fit] {0}: samples={1}, status={2}, rmse={3:G6}, maxRel={4:G6}",
                target.Id,
                fit.SampleCount,
                definition.Status,
                residual.Rmse,
                residual.MaxRelError));
        }

        if (errors.Count > 0)
            return new FormulaFitRunResult(false, null, reports, errors);

        FormulaSet set = FormulaSet.Create(definitions, generatedAt);
        return new FormulaFitRunResult(true, set, reports, errors);
    }
}
