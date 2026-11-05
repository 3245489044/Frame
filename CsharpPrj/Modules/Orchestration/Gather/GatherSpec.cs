#nullable enable

using System;
using System.Collections.Generic;

namespace Modules.Gather;

/// <summary>取数成本档（界判据见领域声明面；本层只承载枚举，零 UI 依赖）。</summary>
public enum GatherCostClass
{
    Low = 0,
    High = 1,
}

/// <summary>
/// 聚拢声明集（<c>Config\gather\*.json</c> 的强类型视图）—— 一个文件承载多视图声明。
/// <para>纪律：本类型只承载**数据**（Need / Stage / Produce），不含取数与判定；执行归 <see cref="GatherHub"/>。</para>
/// </summary>
public sealed record GatherSpecSet(string Schema, IReadOnlyDictionary<string, GatherViewSpec> Views)
{
    /// <summary>按视图键查找（未声明 ⇒ null，调用方须显式回退并可见化）。</summary>
    public GatherViewSpec? Find(string view)
        => Views.TryGetValue(view, out var spec) ? spec : null;
}

/// <summary>
/// 一个数据需求（四元契约的 Need）：域 + 作用域 + 成本档 + 是否可跨拍复用。
/// <para><see cref="Scope"/> 参与复用键 <c>(Domain, Scope)</c>，避免跨作用域影子缓存。</para>
/// </summary>
public sealed record GatherNeedSpec(string Domain, string Scope, GatherCostClass Cost = GatherCostClass.Low, bool Reusable = false);

/// <summary>
/// 一个聚拢阶段（四元契约的 Stage）：需要哪些 need + 依赖哪些阶段。
/// <para>顺序语义**只由 <see cref="Deps"/> 表达**（拓扑序），声明面禁 <c>step/order/sequence</c> 键。</para>
/// </summary>
public sealed record GatherStageSpec(
    string Name,
    IReadOnlyList<string> Needs,
    IReadOnlyList<string> Deps,
    IReadOnlyList<string> Sections);

/// <summary>
/// 一个产物声明（四元契约的 Produce）：出口闭合的判据载体。
/// <para><see cref="Crit"/> ∈ {micro, macro}；<c>macro</c> 必须带 <see cref="Recompute"/>（机械复算命令）。</para>
/// </summary>
public sealed record GatherProduceSpec(
    string Name,
    string Kind,
    string Retention,
    string Crit,
    string? Recompute = null);

/// <summary>
/// 一个视图的聚拢声明：Stage 图 + Need 词表 + Produce 名单。
/// <para>新增视图 = 新增一条本记录（零使用方代码改动）；删声明即装载/门禁红。</para>
/// <para><see cref="Extends"/> / <c>Exclude*</c> 只承载**组合意图**（基视图名 + 排除项），
/// 装载期由 <c>GatherSpecLoader</c> 折叠为 effective 视图（运行期只见展开后的视图）。</para>
/// </summary>
public sealed record GatherViewSpec(
    string View,
    IReadOnlyList<GatherStageSpec> Stages,
    IReadOnlyList<GatherNeedSpec> Needs,
    IReadOnlyList<GatherProduceSpec> Produces,
    string? Extends = null,
    IReadOnlyList<string>? ExcludeStages = null,
    IReadOnlyList<string>? ExcludeNeeds = null,
    IReadOnlyList<string>? ExcludeProduces = null)
{
    /// <summary>按域查 need（未声明 ⇒ null）。</summary>
    public GatherNeedSpec? FindNeed(string domain)
    {
        foreach (var need in Needs)
        {
            if (string.Equals(need.Domain, domain, StringComparison.Ordinal))
            {
                return need;
            }
        }

        return null;
    }

    /// <summary>按名查产物（未声明 ⇒ null ⇒ 写回应被拒绝）。</summary>
    public GatherProduceSpec? FindProduce(string name)
    {
        foreach (var produce in Produces)
        {
            if (string.Equals(produce.Name, name, StringComparison.Ordinal))
            {
                return produce;
            }
        }

        return null;
    }
}
