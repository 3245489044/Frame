namespace Modules.Registry.AutoRegister;

/// <summary>
/// 布局配置文件/工件的定位器 —— 收敛散落的硬编码绝对布局段（如
/// <c>"CsharpPrj","Operation","StateHoldDriven",…,"math-register-config.json"</c>）为两类可复用查找：
///  ① 注入根派生：把"相对布局段"依次与组合根注入的候选根（Route 的 CsharpProjectRoot 等）拼接，命中即取；
///  ② 向上容器回溯：从运行基目录逐级上溯，在每级"容器目录"（containerName）下探测相对段。
/// 两类均只声明"相对段 + 容器名"这一布局约定，不再把绝对路径写进消费方。
/// 搜索根来源经 <see cref="AssemblySourceProvider"/> 注入（纯字符串），Frame 不反向依赖 Route。
/// </summary>
public sealed class LayoutArtifactSearcher
{
    private readonly IReadOnlyList<string> _layoutRoots;

    public LayoutArtifactSearcher(IReadOnlyList<string>? layoutRoots = null)
    {
        _layoutRoots = layoutRoots ?? AssemblySourceProvider.Current.LayoutRoots;
    }

    /// <summary>相对段列表是否含该（相对）目标 —— 无实际语义，仅作帮助方法一致性占位。</summary>
    public IReadOnlyList<string> LayoutRoots => _layoutRoots;

    /// <summary>
    /// 注入根派生：把 <paramref name="segments"/>（相对布局段，如 <c>"Operation","StateHoldDriven","FunctionStorage",…</c>）
    /// 依次与每个注入根拼接，返回首个存在的文件/目录；全部未命中返回 null。
    /// </summary>
    public string? FindUnderRoots(params string[] segments)
    {
        if (segments is null || segments.Length == 0)
            return null;
        foreach (var root in _layoutRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;
            var candidate = Combine(root, segments);
            if (candidate != null && (File.Exists(candidate) || Directory.Exists(candidate)))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// 向上容器回溯：从 <paramref name="startDirectory"/> 逐级上溯，在每一级探测容器目录
    /// （先试该级自身、再试该级下的 <paramref name="containerName"/> 子目录）内的相对段，命中即返回绝对路径。
    /// 未命中返回 null。语义对齐旧 <c>FindConfigUpward</c>（容器名如 <c>"CsharpPrj"</c>）。
    /// </summary>
    public string? FindUpward(string startDirectory, string containerName, params string[] segments)
    {
        if (string.IsNullOrWhiteSpace(startDirectory) || segments is null || segments.Length == 0)
            return null;

        var current = TryGetFullPath(startDirectory);
        while (!string.IsNullOrEmpty(current))
        {
            // 先试本级自身作为容器（如运行目录恰为 CsharpPrj），再试本级下的容器子目录。
            var selfBase = Combine(current, segments);
            if (File.Exists(selfBase))
                return selfBase;

            if (!string.IsNullOrWhiteSpace(containerName))
            {
                var container = Path.Combine(current, containerName);
                var insideContainer = Combine(container, segments);
                if (insideContainer != null && File.Exists(insideContainer))
                    return insideContainer;
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent == null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }
        return null;
    }

    /// <summary>把相对段拼到 <paramref name="baseDir"/> 下返回绝对路径（baseDir 为空返回 null）。</summary>
    public static string? CombineRelative(string baseDir, params string[] segments)
        => Combine(baseDir, segments);

    private static string? Combine(string? baseDir, params string[] segments)
    {
        if (string.IsNullOrWhiteSpace(baseDir) || segments is null || segments.Length == 0)
            return null;
        var parts = new string[segments.Length + 1];
        parts[0] = baseDir;
        Array.Copy(segments, 0, parts, 1, segments.Length);
        try
        {
            return Path.GetFullPath(Path.Combine(parts));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? TryGetFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return null; }
    }
}
