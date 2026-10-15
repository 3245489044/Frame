using System.Collections.Generic;
using Strategy.Catalog.Model;

namespace Strategy.Catalog.Ports;

/// <summary>
/// 目录源（**唯一扩展点**）—— 把「某一类平铺集合」接入分级查询框架：声明轴 + 枚举条目 + 按需给明细。
/// <para>
/// 实现方只提供数据（条目集 = 调用 <c>Enumerate()</c> 的事实；明细 = <c>Describe(key)</c>），
/// **分组 / 检索 / 统计 / 分级呈现一律由框架承担**（<c>CatalogGrouper</c> 单一实现）——
/// 这是"六套目录收敛为一套"的关键：接入新集合 = 增一个实现，不增一套 <c>Xxx_GetDomains</c> API。
/// </para>
/// <para>约定：只读、无副作用、可重复调用；条目数以千级为量级假设（索引只出摘要，明细按需取）。</para>
/// </summary>
public interface ICatalogSource
{
    /// <summary>目录源标识（装配期唯一；大小写不敏感；跨源检索与调用面按此引用）。</summary>
    string SourceId { get; }

    /// <summary>目录源显示名（人类可读；空则回退 <see cref="SourceId"/>）。</summary>
    string Display { get; }

    /// <summary>声明的分类轴（保序；空集 = 只支持平铺索引与检索）。</summary>
    IReadOnlyList<CatalogAxis> Axes { get; }

    /// <summary>枚举条目全集（摘要行；**不含明细**）。</summary>
    IReadOnlyList<CatalogItem> Enumerate();

    /// <summary>
    /// 取条目明细（按需展开）；未命中返回 null（不得抛：未命中是正常查询结果，
    /// 引导由框架经 <c>CatalogHint</c> 给出）。
    /// </summary>
    CatalogDetail? Describe(string key);
}

/// <summary>
/// 目录源注册表端口（唯一查询入口 = <c>CatalogQueryService</c>）。
/// </summary>
public interface ICatalogSourceRegistry
{
    /// <summary>已注册源（保序 = 装配序）。</summary>
    IReadOnlyList<ICatalogSource> Sources { get; }

    /// <summary>按 Id 解析源；未注册返回 null（大小写不敏感）。</summary>
    ICatalogSource? Resolve(string? sourceId);

    /// <summary>是否已注册。</summary>
    bool Contains(string? sourceId);
}
