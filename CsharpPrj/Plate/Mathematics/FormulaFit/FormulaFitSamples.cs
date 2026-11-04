using System.Security.Cryptography;
using MathematicalAlgorithms.Fitting;

namespace MathematicalAlgorithms.FormulaFit;

internal static class FormulaFitSamples
{
    public static bool TryCreate(
        FormulaFitTargetPlan target,
        IReadOnlyList<SweepDocument> sweeps,
        out FitSample[] samples,
        out IReadOnlyList<SweepPoint> usedPoints,
        out IReadOnlySet<string> usedSourcePaths,
        out string? error)
    {
        samples = Array.Empty<FitSample>();
        usedPoints = Array.Empty<SweepPoint>();
        usedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        error = null;

        var sampleList = new List<FitSample>();
        var pointList = new List<SweepPoint>();
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();

        void AddProblem(string message)
        {
            if (problems.Count < 10)
                problems.Add(message);
        }

        foreach (SweepDocument sweep in sweeps)
        {
            bool used = false;
            foreach (SweepPoint point in sweep.Points)
            {
                if (!point.Matches(target.Filter, out _))
                    continue;

                if (!point.TryGetNumber(target.X, "median", out double x, out string? xError))
                {
                    AddProblem($"{sweep.SourcePath}: {xError}");
                    continue;
                }

                if (!point.TryGetNumber(target.Y, target.Aggregate, out double y, out string? yError))
                {
                    AddProblem($"{sweep.SourcePath}: {yError}");
                    continue;
                }

                sampleList.Add(new FitSample(x, y));
                pointList.Add(point);
                used = true;
            }

            if (used)
                sources.Add(sweep.SourcePath);
        }

        if (problems.Count > 0)
        {
            error = string.Join("；", problems);
            return false;
        }

        if (sampleList.Count == 0)
        {
            error = $"target {target.Id} 没有匹配的 sweep 点。";
            return false;
        }

        if (sampleList.Count < target.MinimumSamples)
        {
            error = $"target {target.Id} 样本数 {sampleList.Count} 小于下限 {target.MinimumSamples}。";
            return false;
        }

        samples = sampleList.ToArray();
        usedPoints = pointList;
        usedSourcePaths = sources;
        return true;
    }

    public static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
