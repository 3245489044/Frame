namespace Usharp.Cli;

/// <summary>
/// 命令行源加载：单文件 → 该文件；目录 → 递归 <c>*.ushar</c>（按路径排序，保证编译顺序与输出可复现）。
/// </summary>
public static class UsharpCliSources
{
    /// <summary>Usharp 源扩展名（语言名与扩展名解耦，样例沿用 <c>.ushar</c>）。</summary>
    public const string Extension = ".ushar";

    /// <summary>路径是否存在（文件或目录）。</summary>
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>加载源集（path 仅用于诊断归因）。</summary>
    public static IReadOnlyList<(string Path, string Text)> Load(string path)
    {
        if (Directory.Exists(path))
            return Directory.EnumerateFiles(path, "*" + Extension, SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => (p, File.ReadAllText(p)))
                .ToList();

        return new[] { (path, File.ReadAllText(path)) };
    }
}
