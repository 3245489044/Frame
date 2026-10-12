namespace Usharp.Semantics;

/// <summary>一个待编译源：路径 + 文本 + **按文件**的 lib 归属（null = <see cref="Syntax.UsharpParser.DefaultLib"/>）。</summary>
public sealed record UsharpSourceFile(string Path, string Text, string? Lib = null);

/// <summary>
/// 源加载**单一实现**：路径（文件/目录）→ 源集，并**保留每文件 lib**（跨 lib 的 <c>[CR]</c> 解析口径一致）。
/// <para>
/// 替代此前的三套加载（<c>UsharpParser.ParseFile/ParseAll</c>、<c>UsharpCompilation.CompileFiles</c>、
/// <c>UsharpCliSources.Load</c>）——目录递归按路径序，保证编译顺序与输出可复现。
/// </para>
/// </summary>
public static class UsharpSourceLoader
{
    /// <summary>Usharp 源扩展名（语言名与扩展名解耦，样例沿用 <c>.ushar</c>）。</summary>
    public const string Extension = ".ushar";

    /// <summary>路径是否存在（文件或目录）。</summary>
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>加载路径（文件 → 单源；目录 → 递归 <c>*.ushar</c>，按路径排序）。</summary>
    public static IReadOnlyList<UsharpSourceFile> FromPath(string path, string? lib = null)
    {
        if (Directory.Exists(path))
            return Directory.EnumerateFiles(path, "*" + Extension, SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new UsharpSourceFile(p, File.ReadAllText(p), lib))
                .ToList();

        return new[] { new UsharpSourceFile(path, File.ReadAllText(path), lib) };
    }

    /// <summary>由（路径, lib）清单加载（lib 逐文件保留；空 lib → 缺省库）。</summary>
    public static IReadOnlyList<UsharpSourceFile> FromFiles(IEnumerable<(string Path, string? Lib)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return files.Select(f => new UsharpSourceFile(f.Path, File.ReadAllText(f.Path), f.Lib)).ToList();
    }

    /// <summary>
    /// 由声明库条目构造源集（**盘/库同形**）：诊断归因路径取 <see cref="DeclarationEntry.SourcePath"/>，
    /// 缺省回退 <see cref="DeclarationEntry.LogicalName"/>；保声明序（调用方按需排序）。
    /// </summary>
    public static IReadOnlyList<UsharpSourceFile> FromEntries(IEnumerable<DeclarationEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries
            .Select(e => new UsharpSourceFile(e.SourcePath ?? e.LogicalName, e.Text, e.Lib))
            .ToList();
    }

    /// <summary>
    /// 由声明库装载源集（**单一入口**）：文件库（<see cref="FileDeclarationStore"/>）与数据库库经同一路径，
    /// 装载面不感知来源（对齐「USharp 编排入库」——盘/库是两种实现，不是两条链路）。
    /// </summary>
    public static async Task<IReadOnlyList<UsharpSourceFile>> FromStoreAsync(
        IDeclarationStore store, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var entries = await store.ListAsync(ct).ConfigureAwait(false);
        return FromEntries(entries);
    }
}
