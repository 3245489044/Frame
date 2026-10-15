#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrontEndDriven.Shared.Catalog;
using Strategy.Catalog.Model;
using Strategy.Catalog.Ports;
using UiPathManager = FrontEndDriven.Shared.Utils.PathManager;

namespace FrontEndDriven.Catalog;

/// <summary>
/// 模板目录源（<see cref="ICatalogSource"/>）—— 把框架模板目录（<see cref="FileCatalogService"/> 扫描真源）
/// 接入统一分级查询框架：键 = 相对 id（<c>folder/file.ext</c>），轴 <c>folder</c> = 分类（一级文件夹），
/// 明细 = 分类 / 扩展名 / 全文 / 模板说明。扫描/排序真源不变。
/// </summary>
public sealed class TemplateCatalogSource : ICatalogSource
{
    /// <summary>目录源 Id。</summary>
    public const string Id = "templates";

    /// <summary>分类轴 Id。</summary>
    public const string FolderAxisId = "folder";

    /// <summary>共享单例（无状态；每次现扫，目录不陈旧）。</summary>
    public static readonly TemplateCatalogSource Instance = new();

    /// <inheritdoc />
    public string SourceId => Id;

    /// <inheritdoc />
    public string Display => "模板目录";

    /// <inheritdoc />
    public IReadOnlyList<CatalogAxis> Axes { get; } = new[] { CatalogAxis.Of(FolderAxisId, "分类") };

    /// <summary>默认扫描根 = Agent 请求体模板库。</summary>
    private static FileCatalogSpec Spec()
        => new() { Dir = UiPathManager.GetAgentRunTemplatesRoot() };

    /// <summary>目录条目（扫描 + 稳定排序；真源 = <see cref="FileCatalogService"/>）。</summary>
    public static IReadOnlyList<FileCatalogEntry> Entries()
        => FileCatalogService.OrderEntries(FileCatalogService.Scan(Spec())).ToList();

    /// <inheritdoc />
    public IReadOnlyList<CatalogItem> Enumerate()
        => Entries().Select(e => new CatalogItem(
                e.Id,
                e.Name,
                new[]
                {
                    CatalogAxisValue.Of(FolderAxisId,
                        string.IsNullOrWhiteSpace(e.Category) ? CatalogAxes.MiscValue : e.Category,
                        e.Category),
                },
                string.IsNullOrWhiteSpace(e.Summary) ? e.Action : e.Summary))
            .ToList();

    /// <inheritdoc />
    public CatalogDetail? Describe(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var entry = Entries().FirstOrDefault(e => string.Equals(e.Id, key, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;

        var spec = Spec();
        return CatalogDetail.Of(entry.Id, entry.Name,
            new[]
            {
                new CatalogField("分类", entry.Category),
                new CatalogField("扩展名", Path.GetExtension(entry.Name)),
                new CatalogField("全文", FileCatalogService.LoadText(spec, entry.Id)),
                new CatalogField("模板说明", FileCatalogService.LoadDocText(spec, entry.Id)),
            },
            string.IsNullOrWhiteSpace(entry.Summary) ? entry.Action : entry.Summary,
            "templates");
    }
}
