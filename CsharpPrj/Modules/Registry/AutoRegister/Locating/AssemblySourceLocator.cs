using System.Reflection;

namespace Modules.Registry.AutoRegister;

/// <summary>
/// 程序集源装载引擎 —— 收敛 AutoRegister / 各 *Register 库中散落的
/// "configDir → 运行目录 → 默认 ALC 按名 → 当前目录" 就近装载（及 <see cref="AssemblySourceSearchOptions"/>）。
/// 每次解析都把真实探测过的候选路径带回 <see cref="AssemblySourceResolution.SearchedCandidates"/>，
/// 从根本上消除把"找不到 Mathematics.dll"这类真实缺失包装成 "Target of an invocation" 的黑盒。
/// 依赖方向：本类型在 Frame 侧，搜索基目录为纯字符串；产品组合根可经
/// <see cref="AssemblySourceProvider"/> 注入 Route 派生的权威产物根，Frame 不反向依赖 Route。
/// </summary>
public sealed class AssemblySourceLocator
{
    private readonly AssemblySourceSearchOptions _options;
    private readonly IReadOnlyList<string> _extraDirectories;

    public AssemblySourceLocator(AssemblySourceSearchOptions? options = null)
    {
        _options = options ?? new AssemblySourceSearchOptions();
        // 组合根注入的权威产物目录（追加于调用方给定目录之后、默认 ALC 按名之前）。
        _extraDirectories = AssemblySourceProvider.Current.AssemblySearchDirectories;
    }

    /// <summary>只探测不抛：返回 <see cref="AssemblySourceResolution"/>，未命中时 <see cref="AssemblySourceResolution.Assembly"/> 为 null。</summary>
    public AssemblySourceResolution Locate(string spec)
    {
        var tried = new List<string>();

        if (string.IsNullOrWhiteSpace(spec))
        {
            return Failed(tried, "assembly source 为空");
        }

        var isRooted = Path.IsPathRooted(spec);

        // ① 绝对路径直接装载（若文件存在）。
        if (isRooted)
        {
            if (File.Exists(spec))
            {
                return LoadFrom(spec, tried);
            }
            tried.Add(spec);
        }

        // ② 相对 spec：依次与候选目录拼接（调用方目录 → 注入权威目录）探测。
        var directories = _options.SearchDirectories
            .Concat(_extraDirectories)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in directories)
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            var candidate = SafeCombine(dir, spec);
            if (candidate == null)
                continue;
            tried.Add(candidate);
            if (File.Exists(candidate))
                return LoadFrom(candidate, tried);
        }

        // ③ 默认 ALC 按名（已加载 / deps.json 已登记的程序集）。
        if (_options.TryDefaultAlcByName && !isRooted)
        {
            var simpleName = Path.GetFileNameWithoutExtension(spec);
            if (!string.IsNullOrEmpty(simpleName))
            {
                try
                {
                    var byName = Assembly.Load(new AssemblyName(simpleName));
                    if (byName != null)
                    {
                        return new AssemblySourceResolution
                        {
                            Assembly = byName,
                            LoadedPath = SafeLocation(byName),
                            SearchedCandidates = tried
                        };
                    }
                }
                catch (FileNotFoundException) { /* 目录未命中，落入当前目录兜底 */ }
                catch (BadImageFormatException) { /* 由当前目录兜底接管 */ }
            }
        }

        // ④ 当前目录兜底（兼容旧语义）。
        if (_options.TryCurrentDirectory && !isRooted)
        {
            var cwd = SafeCombine(Environment.CurrentDirectory, spec);
            if (cwd != null)
            {
                tried.Add(cwd);
                if (File.Exists(cwd))
                    return LoadFrom(cwd, tried);
            }
        }

        return Failed(tried, $"未定位到程序集源 '{spec}'（已尝试 {tried.Count} 个候选）");
    }

    /// <summary>
    /// 便捷工厂：以 <paramref name="primaryDirectory"/>（如 register-config 所在目录）为第一候选，
    /// 追加运行目录，构造默认 locator（顺序：主目录 → 运行目录 → 注入权威目录 → 默认 ALC 按名 → 当前目录）。
    /// 语义对齐旧 AutoRegister/各 *Register 的 configDir→baseDir→ALC→CWD 就近装载，但替换为可注入搜索基。
    /// </summary>
    public static AssemblySourceLocator CreateForDirectory(string? primaryDirectory)
    {
        var dirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(primaryDirectory))
            dirs.Add(primaryDirectory);
        if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory)
            && (dirs.Count == 0 || !string.Equals(dirs[^1], AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)))
        {
            dirs.Add(AppContext.BaseDirectory);
        }

        return new AssemblySourceLocator(new AssemblySourceSearchOptions
        {
            SearchDirectories = dirs,
            TryDefaultAlcByName = true,
            TryCurrentDirectory = true
        });
    }

    /// <summary>解析并装载；未命中抛 <see cref="FileNotFoundException"/>（消息含全部候选明细），供上层直接报告根因。</summary>
    public Assembly Resolve(string spec)
    {
        var r = Locate(spec);
        if (r.Succeeded && r.Assembly != null)
            return r.Assembly;

        var details = r.SearchedCandidates.Count == 0
            ? r.Error
            : r.Error + "。候选：\n  " + string.Join("\n  ", r.SearchedCandidates);
        throw new FileNotFoundException(details, spec);
    }

    private static AssemblySourceResolution LoadFrom(string path, List<string> tried)
    {
        try
        {
            var asm = Assembly.LoadFrom(path);
            return new AssemblySourceResolution
            {
                Assembly = asm,
                LoadedPath = path,
                SearchedCandidates = tried
            };
        }
        catch (Exception ex) when (ex is FileLoadException or BadImageFormatException or FileNotFoundException)
        {
            tried.Add(path + $"（装载失败：{ex.Message}）");
            return Failed(tried, $"候选存在但无法装载：{path}。{ex.Message}");
        }
    }

    private static AssemblySourceResolution Failed(List<string> tried, string error)
        => new()
        {
            SearchedCandidates = tried,
            Error = error
        };

    private static string? SafeCombine(string dir, string spec)
    {
        try
        {
            return Path.GetFullPath(Path.Combine(dir, spec));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string SafeLocation(Assembly asm)
    {
        try { return asm.Location; }
        catch { return string.Empty; }
    }
}
