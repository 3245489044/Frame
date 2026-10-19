namespace Modules.Registry;

/// <summary>
/// 扩展部署拓扑（显式注入）—— <b>唯一</b>的“资产可能落在哪里”来源。
/// 禁止在校验内部用 <c>AppContext.BaseDirectory + ..\Shared</c> 推断：开发态共享面在
/// <c>Frame\CsharpPrj\BuildOutput\&lt;Config&gt;</c> / <c>Frame\DataCenter\BuildOutput\&lt;Config&gt;</c>，
/// 发布态在 <c>Runtime\Shared\*</c>，热更新态还会叠加生效代目录。三态根集合由宿主装配，Frame 只消费。
/// </summary>
public sealed record ExtensionTopology(
    IReadOnlyList<string> SharedProbeDirs,
    IReadOnlyList<string> AppProbeDirs)
{
    public static ExtensionTopology Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>());

    /// <summary>按资产简单名在拓扑根内探测 <c>&lt;name&gt;.dll</c>。</summary>
    public bool TryProbe(string simpleName, out string? path)
    {
        foreach (var dir in AllProbeDirs())
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            var candidate = Path.Combine(dir, simpleName + ".dll");
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
        }

        path = null;
        return false;
    }

    /// <summary>共享根 + 宿主根（去重，保持声明顺序）。</summary>
    public IEnumerable<string> AllProbeDirs()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in SharedProbeDirs.Concat(AppProbeDirs))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            var full = Normalize(dir);
            if (seen.Add(full))
                yield return full;
        }
    }

    private static string Normalize(string dir)
    {
        try { return Path.GetFullPath(dir); }
        catch { return dir; }
    }
}
