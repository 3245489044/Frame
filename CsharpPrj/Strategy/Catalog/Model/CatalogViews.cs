using System;
using System.Collections.Generic;

namespace Strategy.Catalog.Model;

/// <summary>
/// 轴目录行（一级索引的一行）—— 某轴取值 + 该取值下的条目数。
/// <para>对齐「部首表」：<c>氵 部 1372 字</c> 就是一行 <see cref="CatalogGroupRow"/>。</para>
/// </summary>
/// <param name="AxisId">轴标识。</param>
/// <param name="AxisDisplay">轴显示名。</param>
/// <param name="Value">轴取值（分组键）。</param>
/// <param name="Display">取值显示名。</param>
/// <param name="Count">该取值下的条目数。</param>
public sealed record CatalogGroupRow(
    string AxisId, string AxisDisplay, string Value, string Display, int Count)
{
    /// <summary>本行分级（恒为 <see cref="CatalogTier.Group"/>；供统一序列化标注）。</summary>
    public CatalogTier Tier => CatalogTier.Group;
}

/// <summary>
/// 一级索引（<see cref="CatalogTier.Index"/>）—— 「有哪些分区、各多少条」+ 常驻条目平铺。
/// <para>
/// <see cref="Pinned"/> = 不随轴分组、恒在索引里可见的入口条目（如控制面工具）；
/// <see cref="Groups"/> = 各轴的目录行；二者构成完整的一级索引，**不含明细**。
/// </para>
/// </summary>
/// <param name="SourceId">目录源标识。</param>
/// <param name="Display">目录源显示名。</param>
/// <param name="Total">条目总数（平铺全集规模）。</param>
/// <param name="Axes">该源声明的轴集（保序）。</param>
/// <param name="Pinned">常驻条目（摘要行）。</param>
/// <param name="Groups">轴目录行（按轴声明序 → 计数降序 → 取值序）。</param>
public sealed record CatalogIndex(
    string SourceId,
    string Display,
    int Total,
    IReadOnlyList<CatalogAxis> Axes,
    IReadOnlyList<CatalogItem> Pinned,
    IReadOnlyList<CatalogGroupRow> Groups)
{
    /// <summary>本视图分级（<see cref="CatalogTier.Index"/>）。</summary>
    public CatalogTier Tier => CatalogTier.Index;

    /// <summary>空索引（源未装配/无条目；避免调用方判空）。</summary>
    public static CatalogIndex Empty(string sourceId, string? display = null)
        => new(sourceId, string.IsNullOrWhiteSpace(display) ? sourceId : display!,
            0, Array.Empty<CatalogAxis>(), Array.Empty<CatalogItem>(), Array.Empty<CatalogGroupRow>());
}

/// <summary>
/// 二级目录（<see cref="CatalogTier.Group"/>）—— 某轴取值下的条目摘要集（对齐「部首页的字表」）。
/// </summary>
/// <param name="SourceId">目录源标识。</param>
/// <param name="AxisId">轴标识。</param>
/// <param name="AxisDisplay">轴显示名。</param>
/// <param name="Value">轴取值。</param>
/// <param name="Display">取值显示名。</param>
/// <param name="Items">条目摘要（序 = 键序）。</param>
public sealed record CatalogList(
    string SourceId, string AxisId, string AxisDisplay, string Value, string Display,
    IReadOnlyList<CatalogItem> Items)
{
    /// <summary>本视图分级（<see cref="CatalogTier.Group"/>）。</summary>
    public CatalogTier Tier => CatalogTier.Group;

    /// <summary>条目数。</summary>
    public int Count => Items?.Count ?? 0;
}
