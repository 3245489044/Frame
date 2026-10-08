namespace Strategy.Observability.SpecHost;

/// <summary>
/// 端口产物归一（G5）：把实现返回的具名映射按**声明字段**补位——声明了但本次未产出的字段补 null。
/// <para>
/// 动机：断言面读「未参与本探针」的字段须得 null（而非缺键 fail-fast），故实现侧逐个手抄
/// 「字段表 + 空基座 + 覆盖」样板（<c>Blank/Overlay</c>）；该样板属通用逻辑，归本件一次完成
/// （字段表唯一真源 = 端口标准 <c>out.fields</c>）。
/// </para>
/// <para>
/// 边界：仅作用于具名映射产物（复合产物）；标量 / 延迟动作 / 问题清单等非映射产物原样返回。
/// 未声明字段（弱校验）→ 原样返回（不猜）。产出的**多余键保留**（交宿主字段契约诊断判「产出 ⊄ 声明」）。
/// </para>
/// </summary>
public static class PortProductNormalizer
{
    /// <summary>声明字段分隔符（端口标准 <c>out.fields</c> / <c>impl.productFields</c> 的书写格式）。</summary>
    private const char Separator = '|';

    /// <summary>
    /// 按声明字段归一具名映射产物：缺失字段按**声明序**补 null；多余键追加在后（不失真）。
    /// 非映射产物 / 空映射 / 无声明字段 → 原样返回。
    /// </summary>
    public static object? Normalize(object? product, string? declaredFields)
    {
        if (product is not IReadOnlyDictionary<string, object?> produced || produced.Count == 0) return product;

        var declared = Split(declaredFields);
        if (declared.Length == 0) return product;

        var normalized = new Dictionary<string, object?>(declared.Length + produced.Count, StringComparer.Ordinal);
        foreach (var name in declared)
            normalized[name] = produced.TryGetValue(name, out var value) ? value : null;

        foreach (var pair in produced)
            if (!normalized.ContainsKey(pair.Key)) normalized[pair.Key] = pair.Value;

        return normalized;
    }

    /// <summary>声明字段串 → 字段名（保声明序；空白与空段剔除；无声明 → 空数组）。</summary>
    private static string[] Split(string? declaredFields)
        => string.IsNullOrWhiteSpace(declaredFields)
            ? []
            : declaredFields.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
