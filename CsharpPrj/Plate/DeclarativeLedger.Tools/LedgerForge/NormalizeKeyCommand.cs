using System.Text.RegularExpressions;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>单份用例源的改写计划（suite 目录名 / 文件名 / 路径 / 改写后全文 / 改写处数）。</summary>
internal sealed record SpecFilePlan(string Suite, string Name, string Path, string Text, int Count);

/// <summary>改写计划（逐文件 + 残留旧 kind + 未登记词元 + 映射条数 / 来源 / 撞键）。</summary>
internal sealed record NormalizeKeyPlan(
    IReadOnlyList<SpecFilePlan> Files,
    IReadOnlyList<string> ResidualKinds,
    IReadOnlyList<string> UnknownKinds,
    int MapCount,
    IReadOnlyList<string> Duplicates,
    string MapSource);

/// <summary>
/// <c>normalize-key</c>：把 <c>.specs</c> 步骤行的**旧 kind** 改写为端口键（映射源 = 数据视图冻结
/// <c>legacyMap</c>，缺失时回退台账 <c>impl.legacyKind</c>）。只动「步骤 kind 位」，其余逐字保留。
/// <para>
/// 三态：<c>--check</c>（残留已端口化旧 kind 即 exit 1）/ <c>--write</c>（落盘）/ 缺省只报告不写盘。
/// 二次运行零变更（幂等）。
/// </para>
/// </summary>
internal static class NormalizeKeyCommand
{
    /// <summary>扫描并规划改写（含归属与计数校验）；不落盘。</summary>
    internal static NormalizeKeyPlan Plan(SpecKindView view, string project, string? suite)
    {
        var casesDirectory = CasesDirectory(view, project);
        if (!Directory.Exists(casesDirectory))
            throw new InvalidOperationException(view.Message("errCasesMissing", casesDirectory));

        var sources = Directory.GetFiles(casesDirectory, "*" + view.SourceExtension, SearchOption.AllDirectories)
            .Where(path => suite is null
                || string.Equals(Path.GetFileName(Path.GetDirectoryName(path)!), suite, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (sources.Length == 0)
            throw new InvalidOperationException(view.Message("errNoSources", project, suite ?? string.Empty));

        var map = view.PortKeyMap(out var duplicates);
        var portKeys = map.Values.ToHashSet(StringComparer.Ordinal);
        var residual = new SortedSet<string>(StringComparer.Ordinal);
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        var files = new List<SpecFilePlan>();

        foreach (var source in sources)
        {
            var parts = Regex.Split(File.ReadAllText(source), view.LineSeparatorPattern);
            var count = 0;

            for (var index = 0; index < parts.Length; index++)
            {
                var match = view.StepLine.Match(parts[index]);
                if (!match.Success) continue;

                var kind = match.Groups["kind"].Value;
                if (view.ExcludedKinds.Contains(kind, StringComparer.Ordinal)) continue;
                if (!map.TryGetValue(kind, out var target))
                {
                    if (!portKeys.Contains(kind)) unknown.Add(kind);
                    continue;
                }

                residual.Add(kind);
                parts[index] = match.Groups["indent"].Value + match.Groups["role"].Value
                    + match.Groups["space"].Value + target + match.Groups["rest"].Value
                    + match.Groups["eol"].Value;
                count++;
            }

            files.Add(new SpecFilePlan(
                Path.GetFileName(Path.GetDirectoryName(source)!) ?? string.Empty,
                Path.GetFileName(source),
                source,
                string.Concat(parts),
                count));
        }

        return new NormalizeKeyPlan(
            files, [.. residual], [.. unknown], map.Count, duplicates,
            view.LegacyMap.Count != 0 ? view.Message("mappingFrozen") : view.Message("mappingLedger"));
    }

    internal static int Run(SpecKindView view, string project, string? suite, bool write, bool check)
    {
        var plan = Plan(view, project, suite);

        Console.WriteLine(view.Message("title"));
        Console.WriteLine(view.Message("targetLine", project, CasesDirectory(view, project), plan.MapCount, plan.MapSource));

        foreach (var file in plan.Files) Console.WriteLine(view.Message("fileRow", file.Suite, file.Count, file.Name));

        Console.WriteLine(view.Message("summaryHeader"));
        Console.WriteLine(view.Message("summaryCounts",
            plan.Files.Count, plan.Files.Count(file => file.Count > 0), plan.Files.Sum(file => file.Count), plan.MapCount));

        if (plan.Duplicates.Count != 0)
        {
            Console.WriteLine(view.Message("duplicates", plan.Duplicates.Count));
            foreach (var duplicate in plan.Duplicates) Console.WriteLine(view.Message("duplicateRow", duplicate));
        }

        if (plan.UnknownKinds.Count != 0)
            Console.WriteLine(view.Message("unknownKinds", string.Join(", ", plan.UnknownKinds)));

        if (check)
        {
            if (plan.ResidualKinds.Count != 0)
            {
                Console.WriteLine(view.Message("checkFail", plan.ResidualKinds.Count, string.Join(", ", plan.ResidualKinds)));
                return 1;
            }

            Console.WriteLine(view.Message("checkPass", plan.Files.Count));
            return 0;
        }

        if (write)
        {
            foreach (var file in plan.Files.Where(file => file.Count > 0)) LedgerBytes.Write(file.Path, file.Text);
            return 0;
        }

        Console.WriteLine(view.Message("writeHint"));
        return 0;
    }

    private static string CasesDirectory(SpecKindView view, string project)
        => Path.Combine(view.AgentTestDirectory, project, view.CasesDirectoryName);
}
