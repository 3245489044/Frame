namespace DataCenter.Protocol.Standards;

/// <summary>
/// 产物字段契约核（**判据唯一实现点**；P3-E / D7 / D8）：「产出字段集 ⊆ 声明字段集」这条规则
/// 此前在四处各写一遍（C13 判据体、内核 <c>Projection\FieldContractValidator</c>、两个守卫用例），
/// 口径（弱校验面 / 去重 / 空键 / 渲染）各自为政。本核把它收敛为**单一实现**：
/// 标准层（C13/C14）与内核运行期（<c>FieldContractValidator</c>）与守卫用例**同调本核**。
/// <para>
/// 分工：本核只做**集合关系 + 文案渲染**（纯函数、零 I/O、零判据编号、零领域词）；
/// 「谁调用 / 报什么编号 / 装载期还是运行期」由消费方决定。
/// </para>
/// <para>
/// 弱校验面（唯一口径，内置于 <see cref="Excess"/>）：**声明集为空 ⇒ 不判** ——
/// 表形态 / 数据驱动键的产物（键由用例数据决定）无法静态完备声明。
/// </para>
/// <para>
/// 方向约定：<paramref name="declared"/> = **声明侧**（契约声明的字段集）、<paramref name="produced"/> = **产出侧**
/// （实现实际产出 / 断言面声明的字段集）；两向对偶判据 = <see cref="Excess"/> 与其反转参数次序。
/// </para>
/// </summary>
public static class OutFieldContract
{
    /// <summary>字段集归一：剔空串 + Ordinal 去重（**保持输入序**，供文案按声明序呈现）。</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<string>();
        foreach (var field in fields)
        {
            if (string.IsNullOrEmpty(field)) continue;
            if (seen.Add(field)) normalized.Add(field);
        }

        return normalized;
    }

    /// <summary>
    /// 产出侧超出声明侧者（**产出序**；空 = 全部有声明）。
    /// 声明集为空 ⇒ 弱校验面，返回空（不判；调用方无需再自判空集）。
    /// </summary>
    public static IReadOnlyList<string> Excess(IEnumerable<string> declared, IEnumerable<string> produced)
    {
        var declaredSet = Normalize(declared).ToHashSet(StringComparer.Ordinal);
        if (declaredSet.Count == 0) return [];

        return Normalize(produced).Where(field => !declaredSet.Contains(field)).ToArray();
    }

    /// <summary>声明侧而未见产出者（**声明序**；告警面，不作阻断）。</summary>
    public static IReadOnlyList<string> Missing(IEnumerable<string> declared, IEnumerable<string> produced)
    {
        var producedSet = Normalize(produced).ToHashSet(StringComparer.Ordinal);

        return Normalize(declared).Where(field => !producedSet.Contains(field)).ToArray();
    }

    /// <summary>声明字段集的文案（空集 → <c>(未声明)</c>；诊断段唯一渲染口径）。</summary>
    public static string Render(IEnumerable<string> declared)
    {
        var fields = Normalize(declared);

        return fields.Count == 0 ? "(未声明)" : string.Join(", ", fields);
    }
}
