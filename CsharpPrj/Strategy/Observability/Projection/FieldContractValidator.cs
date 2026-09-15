using Strategy.Observability.Boundary;

namespace Strategy.Observability.Projection;

/// <summary>
/// 产物字段契约校验（E16）：把「实现产出的键集」与「边界契约声明的字段集」对账，
/// 使字段改名 / 漏字段 / 静默新增在装配与用例之间被机检出来（此前只在用例断言期暴露）。
/// <para>
/// 判据方向：**产出键集 ⊆ 声明字段集**（多出即错——防静默新增未声明字段）；
/// 声明而未见产出只作告警（收集期可缺，不阻断）。均为纯函数，不做断言、不抛异常。
/// </para>
/// <para>
/// 弱校验面：契约**未声明任何字段**时一律不判（口径同「未声明 = 弱校验」的产物字段面）——
/// 表形态 / 数据驱动键的产物（键由用例数据决定）不在此面报错。
/// </para>
/// </summary>
public static class FieldContractValidator
{
    /// <summary>产出键集超出声明字段集的项（错误面；空集 = 全部有声明；契约无声明 → 不判）。</summary>
    public static IReadOnlyList<string> Validate(BoundaryContract contract, IEnumerable<string> producedKeys)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var declared = Declared(contract);
        if (declared.Count == 0) return [];

        return Distinct(producedKeys)
            .Where(key => !declared.Contains(key))
            .Select(key => $"端口 '{contract.Key}' 产出未声明字段 '{key}'（契约字段：{Render(declared)}）")
            .ToArray();
    }

    /// <summary>声明而未见产出的字段（告警面；空集 = 声明全部有产出）。</summary>
    public static IReadOnlyList<string> Missing(BoundaryContract contract, IEnumerable<string> producedKeys)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var declared = Declared(contract);
        var produced = Distinct(producedKeys).ToHashSet(StringComparer.Ordinal);

        return declared
            .Where(field => !produced.Contains(field))
            .Select(field => $"端口 '{contract.Key}' 声明字段 '{field}' 未见产出")
            .ToArray();
    }

    /// <summary>声明字段集（Ordinal 去重）。</summary>
    private static IReadOnlyList<string> Declared(BoundaryContract contract)
        => contract.FieldNames.Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>产出键集（Ordinal 去重 + 空键剔除）。</summary>
    private static IEnumerable<string> Distinct(IEnumerable<string> producedKeys)
        => (producedKeys ?? throw new ArgumentNullException(nameof(producedKeys)))
            .Where(key => !string.IsNullOrEmpty(key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal);

    private static string Render(IReadOnlyList<string> declared)
        => declared.Count == 0 ? "(未声明)" : string.Join(", ", declared);
}
