#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UiPathManager = FrontEndDriven.Shared.Utils.PathManager;

namespace FrontEndDriven.Shared.Catalog;

/// <summary>通用文件目录服务：目录发现/扫描、按相对 id 解析/加载（请求体与说明）。目录根由 PathManager 解析（框架定义），
/// 不绑定任何领域目录；UI 侧经 Ui_*Catalog 通用函数暴露。</summary>
public static class FileCatalogService
{
    /// <summary>解析目录根（Dir 相对项目根，或绝对；空→框架模板根）。</summary>
    public static string? ResolveRoot(FileCatalogSpec? spec)
    {
        var dir = string.IsNullOrWhiteSpace(spec?.Dir) ? UiPathManager.GetTemplatesRoot() : spec!.Dir!;
        if (Path.IsPathRooted(dir))
            return Directory.Exists(dir) ? Path.GetFullPath(dir) : null;
        var root = UiPathManager.GetProjectRoot();
        var abs = Path.GetFullPath(Path.Combine(root, dir));
        return Directory.Exists(abs) ? abs : null;
    }

    /// <summary>递归扫描目录，生成目录条目（分类=一级文件夹；action/summary 仅对 json 模板由内容推导）。
    /// id 携带真实扩展名（category/文件名.扩展名），使同目录同名不同类型文件互不冲突，且可展示全部文件类型。</summary>
    public static List<FileCatalogEntry> Scan(FileCatalogSpec? spec)
    {
        var root = ResolveRoot(spec);
        if (root is null) return new List<FileCatalogEntry>();
        var skip = spec?.SkipFileNames ?? Array.Empty<string>();
        var entries = new List<FileCatalogEntry>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            if (skip.Contains(fileName, StringComparer.OrdinalIgnoreCase) || fileName.StartsWith("_", StringComparison.Ordinal))
                continue;
            var rel = Path.GetRelativePath(root, file);
            var dir = Path.GetDirectoryName(rel);
            var category = string.IsNullOrEmpty(dir) ? "" : dir.Replace('\\', '/');
            var id = string.IsNullOrEmpty(category) ? fileName : category + "/" + fileName;
            var action = "";
            var summary = "";
            // 仅对 json 模板做 action/summary 推导；非 json（如说明 .md）全部展示，内容不进树
            if (string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    // 允许 JSONC（// 注释），支持模板内以注释标注批处理参数
                    var docOpts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip };
                    using var doc = JsonDocument.Parse(File.ReadAllText(file), docOpts);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        var o = doc.RootElement;
                        if (o.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String)
                            action = a.GetString() ?? "";
                        if (o.TryGetProperty("_doc", out var dd) && dd.ValueKind == JsonValueKind.String)
                            summary = dd.GetString() ?? "";
                        else if (o.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String)
                            summary = s.GetString() ?? "";
                    }
                }
                catch { /* 单文件解析失败不阻塞整体扫描 */ }
            }
            entries.Add(new FileCatalogEntry(id, category, fileName, action, summary));
        }
        return entries;
    }

    /// <summary>按相对 id 精确解析文件（id 携带扩展名，按完整相对路径解析，不做旧名/默认/子树回退）。</summary>
    public static string? ResolveFile(FileCatalogSpec? spec, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var root = ResolveRoot(spec);
        if (root is null) return null;
        var clean = id.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var f = Path.Combine(root, clean);
        return File.Exists(f) ? f : null;
    }

    /// <summary>读取文件文本（id 携带扩展名；不存在返回 null）。</summary>
    public static string? LoadText(FileCatalogSpec? spec, string id)
    {
        var file = ResolveFile(spec, id);
        return file is null ? null : File.ReadAllText(file);
    }

    /// <summary>读取说明文档：id 本身为 .md → 直接读该说明；否则精确 {id}.md，缺省回退 &lt;category&gt;/README.md。</summary>
    public static string? LoadDocText(FileCatalogSpec? spec, string id)
    {
        // id 为 .md 说明文件 → 直接读它
        if (string.Equals(Path.GetExtension(id), ".md", StringComparison.OrdinalIgnoreCase))
        {
            var direct = ResolveFile(spec, id);
            if (direct is not null) return File.ReadAllText(direct);
        }

        var docExt = string.IsNullOrWhiteSpace(spec?.DocExtension) ? ".md" : spec!.DocExtension!;
        var file = ResolveFile(spec, id + docExt);
        if (file is null && (spec?.DocFallbackCategoryReadme ?? true))
        {
            var slash = id.IndexOf('/');
            if (slash > 0) file = ResolveFile(spec, id.Substring(0, slash) + "/README" + docExt);
        }
        return file is null ? null : File.ReadAllText(file);
    }

    /// <summary>条目 → 展示串（id 为首段，可被 ExtractId 还原，兼容控件回读整串）。</summary>
    public static string FormatItem(FileCatalogEntry e)
        => $"{e.Id} | {(e.Summary.Length == 0 ? e.Action : e.Summary)}";

    /// <summary>从展示串/裸 id 提取 id（取首段 ` | ` 之前）。</summary>
    public static string ExtractId(string? display)
    {
        var s = (display ?? "").Trim();
        var i = s.IndexOf(" | ", StringComparison.Ordinal);
        return i > 0 ? s.Substring(0, i).Trim() : s;
    }

    /// <summary>目录条目稳定顺序（唯一排序真源 · D3 收敛）：分类升序，再按文件名升序（大小写不敏感）。</summary>
    public static IEnumerable<FileCatalogEntry> OrderEntries(IEnumerable<FileCatalogEntry> entries)
        => entries.OrderBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
                  .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>可用 id 汇总（诊断用）。</summary>
    public static string AvailableIds(FileCatalogSpec? spec)
        => string.Join(" | ", OrderEntries(Scan(spec)).Select(e => e.Id));
}