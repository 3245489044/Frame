#nullable enable

namespace FrontEndDriven.Shared.Catalog;

/// <summary>文件目录条目：Id=相对路径（category/name），Category=一级子目录（空=根平铺），Action/Summary 由文件内容推导。</summary>
public sealed record FileCatalogEntry(string Id, string Category, string Name, string Action, string Summary);