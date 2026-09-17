using BasicMethod.Declarative.Ledger;

namespace DataCenter.Standards;

/// <summary>
/// 台账结构校验（设计文档 §3.2 <c>StandardSchemaValidator</c>）：按 <c>schemas\*.schema.json</c>
/// 做 **结构校验（先结构、后语义）**——结构不过即阻断，不进 C1–C13 语义判据。
/// <para>
/// 薄外观：机制（含关键字子集与诊断文案）唯一实现于
/// <see cref="BasicMethod.Declarative.Ledger.JsonSchemaSubsetValidator"/>；
/// 本件只保留域文案与异常类型身份（<see cref="StandardViolationException"/>）。
/// </para>
/// </summary>
public static class StandardSchemaValidator
{
    /// <summary>结构校验；返回违规明细（空 = 通过）。</summary>
    public static IReadOnlyList<string> Validate(string json, string schemaJson, string? context = null)
        => JsonSchemaSubsetValidator.Validate(json, schemaJson, context);

    /// <summary>结构校验；不通过即抛（阻断装载）。</summary>
    public static void EnsureValid(string json, string schemaJson, string? context = null)
    {
        var violations = Validate(json, schemaJson, context);
        if (violations.Count == 0) return;

        throw new StandardViolationException(
            $"结构校验失败（{violations.Count} 条；先结构后语义）：" +
            Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", violations),
            violations);
    }
}