using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Cli;

/// <summary>
/// 框架一致性审计（R-4/R-12 的长期护栏）：零第三方依赖，供 <c>verify</c> 命令调用。
/// <para>
/// 检查：码表唯一性 / 诊断严重度与码表一致 / 声明 id 重复 / 前缀表计数一致 / 五前缀已注册。
/// 任一不通过 ⇒ 返回 issue（CLI 退出码 1）。
/// </para>
/// </summary>
public static class UsharpFrameworkAudit
{
    /// <summary>执行审计。</summary>
    /// <param name="compilation">编译结果。</param>
    public static IReadOnlyList<string> Run(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var issues = new List<string>();

        try
        {
            UsharpDiagnosticCatalog.AssertUnique();
        }
        catch (Exception ex)
        {
            issues.Add(ex.Message);
        }

        foreach (var diagnostic in compilation.Diagnostics)
        {
            if (!UsharpDiagnosticCatalog.TryGet(diagnostic.Code, out var spec))
            {
                issues.Add($"未登记诊断码 {diagnostic.Code}（{diagnostic.SourceFile}:{diagnostic.LineNumber}）");
                continue;
            }

            if (spec.Severity != diagnostic.Severity)
            {
                issues.Add($"诊断码 {diagnostic.Code} 严重度漂移：码表={spec.Severity}，实发={diagnostic.Severity}"
                    + $"（{diagnostic.SourceFile}:{diagnostic.LineNumber}）");
            }
        }

        ReportDuplicates(compilation.Matrices, m => m.MatrixId, m => m.SourceFile, m => m.LineNumber, "[Matrix]", issues);
        ReportDuplicates(compilation.Flows, f => f.FlowId, f => f.SourceFile, f => f.LineNumber, "[Flow]", issues);
        ReportDuplicates(compilation.KeySpaces, k => k.Id, k => k.SourceFile, k => k.LineNumber, "[KeySpace]", issues);
        ReportDuplicates(compilation.Tables, t => t.TableId, t => t.SourceFile, t => t.LineNumber, "[Table]", issues);
        ReportDuplicates(compilation.Records, r => r.RecordId, r => r.SourceFile, r => r.LineNumber, "[Record]", issues);

        var core = UsharpCorePrefixes.Definitions.Count;
        var domain = UsharpPrefixes.DomainPrefixes.Count;
        var own = UsharpPrefixes.OwnPrefixes.Count;
        if (own != core + domain)
            issues.Add($"前缀表不一致：OwnPrefixes={own}，Core+Domain={core + domain}");

        if (!UsharpParser.PrefixesRegistered())
            issues.Add("UsharpParser.PrefixesRegistered() 未通过（五前缀未全部注册）");

        return issues;
    }

    private static void ReportDuplicates<T>(
        IEnumerable<T> items,
        Func<T, string?> idOf,
        Func<T, string> fileOf,
        Func<T, int> lineOf,
        string label,
        List<string> issues)
        where T : class
    {
        var index = UsharpDeclarationIdIndex<T>.Build(
            items, idOf, item => UsharpSourceRef.At(fileOf(item), lineOf(item)));

        foreach (var group in index.Duplicates)
        {
            var origin = group.Occurrences[0].At;
            foreach (var occurrence in group.Occurrences.Skip(1))
            {
                issues.Add($"{label} id '{group.Id}' 重复声明：{occurrence.At}（首次 {origin}）");
            }
        }
    }
}
