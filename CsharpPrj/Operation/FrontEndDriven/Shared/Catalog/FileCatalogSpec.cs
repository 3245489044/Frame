#nullable enable

namespace FrontEndDriven.Shared.Catalog;

/// <summary>文件目录规格：源目录（相对项目根；空→框架模板根）+ 扫描/加载选项。</summary>
public sealed class FileCatalogSpec
{
    /// <summary>源目录（相对项目根，或绝对路径）；空/空白→框架模板根（PathManager.GetTemplatesRoot）。</summary>
    public string? Dir { get; set; }

    /// <summary>扫描扩展名。当前 Scan 枚举目录下全部文件类型（含说明 .md），本字段保留仅供潜在的按扩展名过滤用途。</summary>
    public string? Extension { get; set; } = ".json";

    /// <summary>说明文档扩展名。</summary>
    public string? DocExtension { get; set; } = ".md";

    /// <summary>说明文档缺省回退到 &lt;category&gt;/README.md。</summary>
    public bool DocFallbackCategoryReadme { get; set; } = true;

    /// <summary>扫描跳过文件名。</summary>
    public string[] SkipFileNames { get; set; } = { "styles.json", "composites.json" };
}