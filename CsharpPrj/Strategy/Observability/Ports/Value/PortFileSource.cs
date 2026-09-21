namespace Strategy.Observability.Ports;

using System.Text.Json;
using DataCenter.Protocol.Standards;
using Strategy.Observability.Diagnostics;

/// <summary>
/// 仓库文件取值单源（G4：<c>from:file{path,json,content}</c>）：以**仓库根**为基准只读加载文件；
/// 绝对路径 / 越界 / 不存在 / 形态非法一律 fail-fast（**禁静默回落**）。
/// <para>
/// 分工：本件只认「相对路径字符串 + 内容形态」（零领域语义）。
/// 根锚口径 = 含 <c>.gitmodules</c> 与 <c>Frame</c> 的目录（Main 仓库根）——
/// **不以 <c>.git</c> 为锚**：<c>Frame</c>/<c>Projects</c> 为子模块、自身即含 <c>.git</c>，
/// 以其为锚会落到子模块根，破坏「各相对路径单一基准」。
/// </para>
/// </summary>
internal static class PortFileSource
{
    /// <summary>内容形态：整文件文本（缺省）。</summary>
    internal const string TextMode = "text";

    /// <summary>内容形态：按行数组（不含行尾符）。</summary>
    internal const string LinesMode = "lines";

    /// <summary>内容形态：JSON 文档（<c>json:true</c> 的等价写法）。</summary>
    internal const string JsonMode = "json";

    private static readonly Lazy<string> RootPath = new(Locate);

    /// <summary>仓库根（探测失败即抛；全部 <c>file</c> 取值的唯一基准）。</summary>
    internal static string Root => RootPath.Value;

    /// <summary>内容形态解析（<c>content</c> 与 <c>json</c> 同时给出且相互矛盾 → fail-fast）。</summary>
    internal static string ModeOf(ValueSourceStandard spec, string portKey, string context)
    {
        var declared = spec.Content is { Length: > 0 } content ? content : null;

        if (spec.Json is { } flag)
        {
            var implied = flag ? JsonMode : TextMode;

            if (declared is not null && !string.Equals(declared, implied, StringComparison.Ordinal))
                throw new PortBindingViolationException(
                    portKey,
                    $"端口 '{portKey}' 的 {context} 的 file.json={flag.ToString().ToLowerInvariant()} 与 " +
                    $"file.content='{declared}' 矛盾（二选一或保持一致；<c>json:true</c> ⇔ <c>content:\"json\"</c>）");

            declared = implied;
        }

        declared ??= TextMode;

        return declared is TextMode or LinesMode or JsonMode
            ? declared
            : throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 {context} 的 file.content '{declared}' 未知（text|lines|json）");
    }

    /// <summary>读取仓库相对文件（按形态给出文本 / 行数组 / JSON 根元素）。</summary>
    internal static object? Read(string relativePath, string mode, string portKey, string context)
    {
        var full = Resolve(relativePath, portKey, context);

        return mode switch
        {
            JsonMode => Json(full, portKey, context),
            LinesMode => File.ReadAllLines(full),
            _ => File.ReadAllText(full),
        };
    }

    /// <summary>仓库相对路径 → 绝对路径（绝对路径 / 越界 / 不存在即抛）。</summary>
    private static string Resolve(string relativePath, string portKey, string context)
    {
        if (Path.IsPathRooted(relativePath))
            throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 {context} 的 file.path '{relativePath}' 须为仓库相对路径（禁绝对路径）");

        var root = Root;
        var full = Path.GetFullPath(Path.Combine(root, relativePath));

        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 {context} 的 file.path '{relativePath}' 越出仓库根（{root}）");

        return File.Exists(full)
            ? full
            : throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 {context} 的 file.path 文件不存在：{full}（仓库相对 '{relativePath}'）");
    }

    /// <summary>JSON 解析（首个文档的根元素；解析失败即抛，异常含文件定位）。</summary>
    private static object Json(string full, string portKey, string context)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(full));
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 {context} 的 file.path JSON 解析失败（{full}）：{ex.Message}");
        }
    }

    /// <summary>仓库根探测（自输出目录向上找标记目录；找不到即抛，禁静默回落）。</summary>
    private static string Locate()
    {
        var current = AppContext.BaseDirectory;

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current, ".gitmodules"))
                && Directory.Exists(Path.Combine(current, "Frame")))
                return current;

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException(
            $"[port:file] 仓库根探测失败（自输出目录 {AppContext.BaseDirectory} 向上未找到含 " +
            ".gitmodules 与 Frame 的目录）：file 来源须以仓库为基准，禁静默回落");
    }
}
