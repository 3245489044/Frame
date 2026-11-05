using ContractFramework.Abstractions;
using ContractFramework.Catalog;

namespace ContractFramework.Diagnostics;

/// <summary>
/// 结构化诊断出口（类别 + 消息 + 契约 Id）——对齐既有 <c>category:message</c> 风格。
/// </summary>
public sealed record ContractDiagnostic(string Category, string Message, ContractId? ContractId = null)
{
    public override string ToString()
        => ContractId is null ? $"[{Category}] {Message}" : $"[{Category}] {ContractId}: {Message}";
}

/// <summary>
/// 契约诊断聚合（未判定清单 + 违例清单 + 目录问题）。
/// </summary>
public static class ContractDiagnostics
{
    /// <summary>未判定（<c>⊥</c>）类别。</summary>
    public const string UnverifiableCategory = "ContractUnverifiable";

    /// <summary>违例类别。</summary>
    public const string ViolationCategory = "ContractViolation";

    /// <summary>目录加载问题类别。</summary>
    public const string CatalogCategory = "ContractCatalog";

    /// <summary>由执行结果生成诊断（违例 + 未判定）。</summary>
    public static IReadOnlyList<ContractDiagnostic> FromResult(ContractResult? result)
    {
        var list = new List<ContractDiagnostic>();
        if (result is null)
        {
            return list;
        }

        foreach (var violation in result.Violations)
        {
            list.Add(new ContractDiagnostic(ViolationCategory, $"{violation.Target}: {violation.Detail}", violation.ContractId));
        }

        foreach (var unverifiable in result.Unverifiable)
        {
            list.Add(new ContractDiagnostic(UnverifiableCategory, unverifiable.Reason, unverifiable.ContractId));
        }

        return list;
    }

    /// <summary>由目录生成诊断（解析失败 / 重复 Id 等）。</summary>
    public static IReadOnlyList<ContractDiagnostic> FromCatalog(ContractCatalog? catalog)
    {
        var list = new List<ContractDiagnostic>();
        if (catalog is null)
        {
            return list;
        }

        foreach (var issue in catalog.Issues)
        {
            list.Add(new ContractDiagnostic(CatalogCategory, $"{issue.Kind}: {issue.Detail}"));
        }

        return list;
    }

    /// <summary>格式化为多行文本（供日志 / 门禁输出）。</summary>
    public static string Format(IEnumerable<ContractDiagnostic> diagnostics)
    {
        var list = (diagnostics ?? []).ToList();
        return list.Count == 0 ? "(无诊断)" : string.Join(Environment.NewLine, list.Select(d => d.ToString()));
    }
}
