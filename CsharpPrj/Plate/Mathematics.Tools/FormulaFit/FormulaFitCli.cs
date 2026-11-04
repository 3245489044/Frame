using System.Globalization;
using MathematicalAlgorithms.FormulaFit;

namespace MathematicalAlgorithms.FormulaFit.Cli;

/// <summary>离线产出器入口：解析参数、加载计划/sweep、拟合、写公式集。</summary>
public static class FormulaFitCli
{
    public static int Run(string[] args) => Run(args, Console.Out, Console.Error);

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (!FormulaFitArguments.TryParse(args, out FormulaFitArguments? arguments, out string? parseError))
        {
            error.WriteLine($"[error] {parseError}");
            output.WriteLine(FormulaFitArguments.Usage);
            return 1;
        }

        if (arguments!.ShowHelp)
        {
            output.WriteLine(FormulaFitArguments.Usage);
            return 0;
        }

        try
        {
            FormulaFitPlan plan = FormulaFitPlan.Load(arguments.DefinitionsPath);
            SweepDocument[] sweeps = arguments.Inputs.Select(SweepDocument.Load).ToArray();
            DateTimeOffset generatedAt = arguments.GeneratedAt ?? DateTimeOffset.UtcNow;

            FormulaFitRunResult result = FormulaFitRunner.Run(
                plan,
                sweeps,
                generatedAt,
                arguments.Verbose ? output : TextWriter.Null);

            foreach (FormulaFitTargetReport report in result.Targets)
            {
                output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: samples={1}, status={2}, rmse={3:G6}, maxAbs={4:G6}, maxRel={5:G6}{6}",
                    report.Id,
                    report.Samples,
                    report.Status,
                    report.Rmse,
                    report.MaxAbsError,
                    report.MaxRelError,
                    report.Detail is null ? string.Empty : $", detail={report.Detail}"));
            }

            if (!result.Success || result.FormulaSet is null)
            {
                foreach (string message in result.Errors)
                    error.WriteLine($"[error] {message}");

                return 2;
            }

            string outputFile = arguments.ResolveOutputFile();
            result.FormulaSet.Save(outputFile);
            output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "已写出 {0} 条公式：{1}",
                result.FormulaSet.Formulas.Count,
                outputFile));

            return 0;
        }
        catch (Exception ex)
        {
            error.WriteLine(arguments.Verbose ? ex.ToString() : $"[error] {ex.Message}");
            return 2;
        }
    }
}
