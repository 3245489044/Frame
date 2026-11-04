using System.Globalization;

namespace MathematicalAlgorithms.FormulaFit.Cli;

/// <summary>命令行参数：--input（可重复）--defs --out --now --verbose --help。</summary>
public sealed record FormulaFitArguments(
    IReadOnlyList<string> Inputs,
    string DefinitionsPath,
    string OutputPath,
    DateTimeOffset? GeneratedAt,
    bool Verbose,
    bool ShowHelp)
{
    public const string Usage =
        "用法: MathematicalAlgorithms.FormulaFit --input <sweep.json> [--input <sweep.json> ...] --defs <plan.json> --out <file.json|目录> [--now <ISO8601>] [--verbose]";

    public string ResolveOutputFile()
    {
        string full = Path.GetFullPath(OutputPath);
        return full.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? full
            : Path.Combine(full, "metric-formulas.json");
    }

    public static bool TryParse(
        string[] args,
        out FormulaFitArguments? parsed,
        out string? error)
    {
        parsed = null;
        error = null;

        var inputs = new List<string>();
        string? definitions = null;
        string? output = null;
        DateTimeOffset? generatedAt = null;
        bool verbose = false;
        bool showHelp = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;

                case "--verbose":
                    verbose = true;
                    break;

                case "--input":
                    if (!TryReadValue(args, ref i, out string inputValue, out error))
                        return false;
                    inputs.Add(inputValue);
                    break;

                case "--defs":
                    if (!TryReadValue(args, ref i, out definitions, out error))
                        return false;
                    break;

                case "--out":
                    if (!TryReadValue(args, ref i, out output, out error))
                        return false;
                    break;

                case "--now":
                {
                    if (!TryReadValue(args, ref i, out string nowValue, out error))
                        return false;

                    if (!DateTimeOffset.TryParse(
                            nowValue,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind,
                            out DateTimeOffset parsedNow))
                    {
                        error = $"--now 不是合法 ISO8601 时间：{nowValue}。";
                        return false;
                    }

                    generatedAt = parsedNow;
                    break;
                }

                default:
                    if (arg.StartsWith("--input=", StringComparison.Ordinal))
                    {
                        inputs.Add(arg["--input=".Length..]);
                    }
                    else if (arg.StartsWith("--defs=", StringComparison.Ordinal))
                    {
                        definitions = arg["--defs=".Length..];
                    }
                    else if (arg.StartsWith("--out=", StringComparison.Ordinal))
                    {
                        output = arg["--out=".Length..];
                    }
                    else if (arg.StartsWith("--now=", StringComparison.Ordinal))
                    {
                        string nowValue = arg["--now=".Length..];
                        if (!DateTimeOffset.TryParse(
                                nowValue,
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.RoundtripKind,
                                out DateTimeOffset parsedNow))
                        {
                            error = $"--now 不是合法 ISO8601 时间：{nowValue}。";
                            return false;
                        }

                        generatedAt = parsedNow;
                    }
                    else
                    {
                        error = $"未知参数：{arg}。";
                        return false;
                    }

                    break;
            }
        }

        if (showHelp)
        {
            parsed = new FormulaFitArguments(
                Array.Empty<string>(),
                string.Empty,
                string.Empty,
                null,
                verbose,
                true);
            return true;
        }

        if (inputs.Count == 0)
        {
            error = "至少需要一个 --input。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(definitions))
        {
            error = "缺少 --defs。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            error = "缺少 --out。";
            return false;
        }

        parsed = new FormulaFitArguments(
            inputs,
            definitions,
            output,
            generatedAt,
            verbose,
            false);
        return true;
    }

    private static bool TryReadValue(
        string[] args,
        ref int index,
        out string value,
        out string? error)
    {
        value = string.Empty;
        error = null;

        if (index + 1 >= args.Length)
        {
            error = $"参数 {args[index]} 缺少值。";
            return false;
        }

        value = args[++index];
        return true;
    }
}
