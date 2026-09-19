using Strategy.Observability.Boundary;

namespace Strategy.Observability.SpecHost;

/// <summary>
/// 边界契约投影注入点（M2 装载期解析退场）：执行面通用件不引 <c>DataCenter.Standards</c>（装载层），
/// 台账 JSON → 边界契约的装载与投影由**能同见标准层的一侧**（测试侧投影适配器 / 产品装配侧）装配；
/// 未装配即抛可定位异常（不静默空契约面）。
/// </summary>
public static class SpecBoundaryContracts
{
    /// <summary>装配注入点（json → 边界契约；幂等：重复装配以后者为准）。</summary>
    public static Func<string, IReadOnlyList<BoundaryContract>>? Projection { get; set; }

    /// <summary>按 JSON 文本投影为边界契约；未装配投影即抛（防「静默空契约面」）。</summary>
    public static IReadOnlyList<BoundaryContract> Project(string json)
        => (Projection ?? throw new InvalidOperationException(
            "[spec:boundary] 边界契约投影未装配（SpecBoundaryContracts.Projection）："
            + "执行面通用件不解析台账 JSON —— 须由能同见标准层的一侧装配投影（测试侧投影适配器 / 产品装配侧）"))
        .Invoke(json);
}
