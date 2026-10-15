using System;
using System.Collections.Generic;
using System.Linq;
using Strategy.Catalog.Model;
using Strategy.Catalog.Ports;

namespace Strategy.Catalog.Core;

/// <summary>
/// 分级查询服务（**唯一查询面 / 单一实现**）—— 对上只暴露五件事：
/// <c>索引（Index）→ 目录（List）→ 明细（Describe）→ 检索（Search）→ 统计（Stats）</c>，
/// 外加最外层的「有哪些目录（Sources）」。
/// <para>
/// 消费方（工具面 / UI / 脚本 / LLM）只依赖本服务，**不直接遍历 source**：
/// 这样"增一套目录"只需注册 <see cref="ICatalogSource"/>，查询契约零变化。
/// </para>
/// </summary>
public sealed class CatalogQueryService
{
    /// <summary>进程默认实例（挂 <see cref="CatalogRegistry.Default"/>；宿主可用自建实例）。</summary>
    public static CatalogQueryService Shared { get; } = new(CatalogRegistry.Default);

    private readonly ICatalogSourceRegistry _registry;

    public CatalogQueryService(ICatalogSourceRegistry registry)
        => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    /// <summary>底层注册表（装配/诊断用）。</summary>
    public ICatalogSourceRegistry Registry => _registry;

    /// <summary>最外层索引：有哪些目录可查（源概要；无源返回空表）。</summary>
    public IReadOnlyList<CatalogSourceInfo> Sources()
        => _registry.Sources
            .Where(s => s is not null)
            .Select(CatalogGrouper.Info)
            .OrderBy(i => i.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>一级索引（源未注册抛 UnknownCatalogSourceException）。</summary>
    public CatalogIndex Index(string? sourceId)
        => CatalogGrouper.Index(Require(sourceId));

    /// <summary>二级目录（按轴取值列条目摘要）。</summary>
    public CatalogList List(string? sourceId, string? axisId, string? value)
        => CatalogGrouper.List(Require(sourceId), axisId, value);

    /// <summary>明细（按需展开）；未命中返回 null。</summary>
    public CatalogDetail? Describe(string? sourceId, string? key)
        => CatalogGrouper.Describe(Require(sourceId), key);

    /// <summary>明细 + 未命中引导（未命中时 <paramref name="hint"/> 给出候选键；命中时 hint 为空）。</summary>
    public CatalogDetail? DescribeWithHint(string? sourceId, string? key, out CatalogHint hint)
    {
        var source = Require(sourceId);
        var detail = CatalogGrouper.Describe(source, key);
        hint = detail is null ? CatalogGrouper.HintFor(source, key) : CatalogHint.None;
        return detail;
    }

    /// <summary>单源检索（上限 + 截断可见）。</summary>
    public CatalogSearchResult Search(string? sourceId, string? query, int limit = CatalogGrouper.DefaultSearchLimit)
        => CatalogGrouper.Search(Require(sourceId), query, limit);

    /// <summary>跨源检索（"我不确定在哪本册子里"的兜底入口）。</summary>
    public CatalogSearchResult SearchAll(string? query, int limit = CatalogGrouper.DefaultSearchLimit)
        => CatalogGrouper.SearchAll(_registry.Sources, query, limit);

    /// <summary>统计概览。</summary>
    public CatalogStats Stats(string? sourceId, int largestGroupLimit = CatalogGrouper.DefaultLargestGroupLimit)
        => CatalogGrouper.Stats(Require(sourceId), largestGroupLimit);

    private ICatalogSource Require(string? sourceId)
        => _registry.Resolve(sourceId)
           ?? throw new Diagnostics.UnknownCatalogSourceException(sourceId ?? string.Empty,
               _registry.Sources.Where(s => s is not null).Select(s => s.SourceId).ToList());
}
