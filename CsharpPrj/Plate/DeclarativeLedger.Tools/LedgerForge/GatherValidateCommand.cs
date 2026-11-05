using System.Text.Json;
using System.Text.RegularExpressions;
using Modules.Gather;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>
/// 聚拢声明校验 CLI（A1/D10）：**唯一实现** = <see cref="GatherSpecLoader"/>（Validate + ResolveInheritance）。
/// <para>
/// 本命令只做 IO 编排（目录枚举 → 逐文件装载 → issues 汇总），不复制任何声明解析或折叠逻辑。
/// 规则号一律沿用 C# 的 V1–V15；PowerShell 侧不再维护 S1–S11。
/// </para>
/// 退出码：0 无违规；1 有违规；2 声明目录缺失（不可判定，非通过）。
/// </summary>
internal static class GatherValidateCommand
{
    private static readonly Regex CodePattern = new(@"\[(V\d+)\]", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static int Run(string? directory, string? outPath)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            Console.Error.WriteLine($"[ledger:forge] gather-validate 声明目录缺失: {directory}");
            return 2;
        }

        var files = Directory
            .GetFiles(directory, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var issues = new List<GatherIssue>();
        foreach (var file in files)
        {
            try
            {
                var text = File.ReadAllText(file);
                // 唯一校验入口：结构 + 组合折叠 + 合格性（V1–V15）。
                GatherSpecLoader.LoadFromText(text, file);
            }
            catch (InvalidOperationException exception)
            {
                AddIssues(exception.Message, file, issues);
            }
            catch (Exception exception)
            {
                issues.Add(new GatherIssue("LOAD", file, exception.Message));
            }
        }

        var payload = new GatherValidateResult(issues.Count == 0, files.Length, issues);
        var json = JsonSerializer.Serialize(payload, OutputOptions);
        if (string.IsNullOrWhiteSpace(outPath))
        {
            Console.Out.WriteLine(json);
        }
        else
        {
            File.WriteAllText(outPath, json);
        }

        return issues.Count == 0 ? 0 : 1;
    }

    /// <summary>异常文案 → 逐条 issue；同一文件的多条 V 规则以「；」拼接（保留 C# 原始编号）。</summary>
    private static void AddIssues(string message, string file, List<GatherIssue> issues)
    {
        foreach (var segment in message.Split('；', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = CodePattern.Match(segment);
            if (!match.Success)
            {
                issues.Add(new GatherIssue("LOAD", file, segment));
                continue;
            }

            var code = match.Groups[1].Value;
            var text = segment;
            // 去掉「聚拢声明…（path）：」这类统一前缀，只留下判据正文。
            var bracket = text.IndexOf('[');
            if (bracket > 0)
            {
                text = text[bracket..];
            }

            issues.Add(new GatherIssue(code, file, text));
        }
    }

    private sealed record GatherIssue(string Code, string Path, string Message);

    private sealed record GatherValidateResult(bool Ok, int FileCount, IReadOnlyList<GatherIssue> Issues);
}
