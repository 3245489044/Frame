using DataCenter.Protocol.Standards;
using Strategy.Observability.Boundary;

namespace Strategy.Observability.Projection;

/// <summary>
/// 产物字段契约校验（E16）：把「实现产出的键集」与「边界契约声明的字段集」对账，
/// 使字段改名 / 漏字段 / 静默新增在装配与用例之间被机检出来（此前只在用例断言期暴露）。
/// <para>
/// 判据**唯一实现 = <see cref="OutFieldContract"/>**（DataCenter.Protocol 契约层，零依赖）：本件只做
/// 「边界契约 → 声明字段集」的提取与内核文案渲染；集合关系（含弱校验面）一律委托核——
/// 标准层装载期判据（C13 / C14）与守卫用例同调该核（P3-E / D7 / D8）。
/// </para>
/// <para>
/// 判据方向：**产出键集 ⊆ 声明字段集**（多出即错——防静默新增未声明字段）；
/// 声明而未见产出只作告警（收集期可缺，不阻断）。均为纯函数，不做断言、不抛异常。
/// </para>
/// <para>
/// 弱校验面：契约**未声明任何字段**时一律不判（口径在核内，与标准层一致）——
/// 表形态 / 数据驱动键的产物（键由用例数据决定）不在此面报错。
/// </para>
/// </summary>
public static class FieldContractValidator
{
    /// <summary>产出键集超出声明字段集的项（错误面；空集 = 全部有声明；契约无声明 → 不判）。</summary>
    public static IReadOnlyList<string> Validate(BoundaryContract contract, IEnumerable<string> producedKeys)
    {
        PortGuard.Required(contract, nameof(contract));
        var declared = Declared(contract);

        return OutFieldContract
            .Excess(declared, Ordered(producedKeys))
            .Select(key => $"端口 '{contract.Key}' 产出未声明字段 '{key}'（契约字段：{OutFieldContract.Render(declared)}）")
            .ToArray();
    }

    /// <summary>声明而未见产出的字段（告警面；空集 = 声明全部有产出）。</summary>
    public static IReadOnlyList<string> Missing(BoundaryContract contract, IEnumerable<string> producedKeys)
    {
        PortGuard.Required(contract, nameof(contract));
        var declared = Declared(contract);

        return OutFieldContract
            .Missing(declared, Ordered(producedKeys))
            .Select(field => $"端口 '{contract.Key}' 声明字段 '{field}' 未见产出")
            .ToArray();
    }

    /// <summary>声明字段集（核归一：剔空 + Ordinal 去重，保持契约声明序）。</summary>
    private static IReadOnlyList<string> Declared(BoundaryContract contract)
        => OutFieldContract.Normalize(contract.FieldNames);

    /// <summary>产出键集（核归一 + Ordinal 排序：诊断序确定性）。</summary>
    private static IEnumerable<string> Ordered(IEnumerable<string> producedKeys)
        => OutFieldContract
            .Normalize(PortGuard.Required(producedKeys, nameof(producedKeys)))
            .OrderBy(key => key, StringComparer.Ordinal);
}
