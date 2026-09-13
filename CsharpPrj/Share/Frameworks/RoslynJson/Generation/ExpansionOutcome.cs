using System.Collections.Generic;
using Share.LogicComposition.Conversion.Models;

namespace Share.RoslynJson.Generation;

/// <summary>
/// 展开结果：诊断（全量，含格式级 + 语义级 + 发射级）与产物文本。
/// <see cref="IsSuccess"/> ⇔ 产物已产出（Error 级诊断会阻断产物）。
/// </summary>
public sealed class ExpansionOutcome
{
    /// <summary>全量诊断（顺序：格式级 → 语义级 → 发射级）。</summary>
    public IReadOnlyList<ConversionDiagnostic> Diagnostics { get; }

    /// <summary>生成物文本（失败时为 null）。</summary>
    public string? Artifact { get; }

    /// <summary>是否成功（有产物即成功）。</summary>
    public bool IsSuccess => Artifact is not null;

    /// <summary>构造结果。</summary>
    public ExpansionOutcome(IReadOnlyList<ConversionDiagnostic> diagnostics, string? artifact)
    {
        Diagnostics = diagnostics;
        Artifact = artifact;
    }
}
