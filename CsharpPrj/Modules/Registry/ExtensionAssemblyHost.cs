using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Modules.Registry;

/// <summary>
/// 扩展程序集宿主 —— 插件层装载入口（通用化）。
/// 扩展层（Agent.* / GameEditor.* / PrjBridge.dll 等）不在 FileDriven 的 deps.json，
/// 由各产品经 DeploySelfToRuntime 聚合输出整体部署到运行目录，并携带各自的 deps.json（扩展闭包清单）。
/// 启动期用 AssemblyDependencyResolver 定位这些清单，建专属 scoped ALC 按 .NET 原生语义装载
/// （传递依赖/版本/native 均由平台处理）。缺失清单时保持空 ALC，降级（契合 TryRegister* 约定）。
/// </summary>
public static class ExtensionAssemblyHost
{
    private const string DefaultManifest = "PrjBridge.deps.json";
    private const string IndexFileName = "ExtensionLoaderConfig.json";

    private static readonly object _gate = new();
    private static ExtensionLoadContext? _context;
    private static bool _initialized;
    private static List<string>? _manifests;

    /// <summary>是否已初始化。</summary>
    public static bool IsInitialized => _initialized;

    /// <summary>当前装载的全部扩展 deps.json 清单（绝对路径；未部署时为空）。</summary>
    public static IReadOnlyList<string> Manifests => _manifests ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>主扩展 deps.json（首个清单；未部署时为空串）。兼容旧取用方。</summary>
    public static string DepsJsonPath => (_manifests != null && _manifests.Count > 0) ? _manifests[0] : "";

    /// <summary>
    /// 启动期调用一次（幂等）：解析扩展 deps.json 清单集合并建 scoped ALC。
    /// 清单来源优先级：
    ///   1. 显式传入的 depsJsonPaths（按名可相对运行目录）；
    ///   2. 运行目录的 ExtensionLoaderConfig.json（JSON 字符串数组，每项为一个 .deps.json 文件名）；
    ///   3. 默认回退单个 PrjBridge.deps.json（向后兼容）。
    /// </summary>
    public static void Initialize(params string[] depsJsonPaths)
    {
        lock (_gate)
        {
            if (_initialized)
                return;
            _initialized = true;

            _manifests = ResolveManifests(depsJsonPaths);
            _context = _manifests.Count > 0 ? new ExtensionLoadContext(_manifests) : null; // 未部署 → 降级
        }
    }

    /// <summary>解析装配清单集合：显式路径 → 索引文件 → 默认回退。</summary>
    private static List<string> ResolveManifests(string[] explicitPaths)
    {
        var result = new List<string>();

        foreach (var p in explicitPaths)
        {
            if (string.IsNullOrWhiteSpace(p))
                continue;
            var full = Path.IsPathRooted(p) ? p : Path.Combine(AppContext.BaseDirectory, p);
            if (File.Exists(full) && !result.Contains(full, StringComparer.OrdinalIgnoreCase))
                result.Add(full);
        }
        if (result.Count > 0)
            return result;

        var index = Path.Combine(AppContext.BaseDirectory, IndexFileName);
        if (File.Exists(index))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(index));
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        var name = item.GetString();
                        if (string.IsNullOrWhiteSpace(name))
                            continue;
                        var full = Path.IsPathRooted(name) ? name : Path.Combine(AppContext.BaseDirectory, name);
                        if (File.Exists(full) && !result.Contains(full, StringComparer.OrdinalIgnoreCase))
                            result.Add(full);
                    }
            }
            catch
            {
                // 索引解析失败 → 落入默认回退
            }
        }

        if (result.Count == 0)
        {
            var fallback = Path.Combine(AppContext.BaseDirectory, DefaultManifest);
            if (File.Exists(fallback))
                result.Add(fallback);
        }

        return result;
    }

    /// <summary>按程序集限定名在 scoped ALC 里解析类型（显式发现，不撞 Type.GetType 运气）。</summary>
    public static Type? ResolveType(string typeStr)
    {
        var context = EnsureInitialized();
        if (context == null)
            return null;

        var comma = typeStr.IndexOf(',');
        if (comma < 0)
            return null;

        var fullTypeName = typeStr.Substring(0, comma).Trim();
        var asmName = typeStr.Substring(comma + 1).Trim();
        var nextComma = asmName.IndexOf(',');
        if (nextComma >= 0)
            asmName = asmName.Substring(0, nextComma).Trim();

        if (fullTypeName.Length == 0 || asmName.Length == 0)
            return null;

        try
        {
            var asm = context.LoadFromAssemblyName(new AssemblyName(asmName));
            return asm.GetType(fullTypeName, throwOnError: false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>按程序集简单名在 scoped ALC 里加载（供 PrjBridgeWiringHook 等反射装载用）。</summary>
    public static Assembly? Load(string simpleName)
    {
        var context = EnsureInitialized();
        if (context == null)
            return null;

        try
        {
            return context.LoadFromAssemblyName(new AssemblyName(simpleName));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 部署校验：解析全部扩展 deps.json 的 runtime 资产，报告磁盘缺失项（快速定位缺哪个依赖）。
    /// 仅诊断用，不抛异常。
    /// </summary>
    public static IReadOnlyList<string> ValidateDeployment()
    {
        var missing = new List<string>();
        var manifests = InitializeOnceAndGetManifests();
        if (manifests.Count == 0)
            return missing;

        foreach (var depsPath in manifests)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(depsPath));
                var root = doc.RootElement;
                if (!root.TryGetProperty("libraries", out var libs))
                    continue;

                foreach (var lib in libs.EnumerateObject())
                {
                    if (!lib.Value.TryGetProperty("runtime", out var runtime))
                        continue;
                    foreach (var asset in runtime.EnumerateObject())
                    {
                        var rel = asset.Name;
                        if (!rel.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, rel)))
                            missing.Add(rel);
                    }
                }
            }
            catch
            {
                // 解析失败仅影响诊断
            }
        }

        return missing;
    }

    private static IReadOnlyList<string> InitializeOnceAndGetManifests()
    {
        if (!_initialized)
            Initialize();
        return Manifests;
    }

    private static ExtensionLoadContext? EnsureInitialized()
    {
        if (!_initialized)
            Initialize();
        return _context;
    }
}

/// <summary>
/// 专属扩展装载上下文：以多个 AssemblyDependencyResolver（一个清单一个）解析。
/// Load 覆写先复用默认 ALC 已加载的同名程序集（防 FileDriven.Abstractions 等类型身份分裂），
/// 再按清单顺序 fallback 定位 DLL；所有清单找不到返回 null → 交还默认 ALC。
/// </summary>
public sealed class ExtensionLoadContext : AssemblyLoadContext
{
    private readonly List<AssemblyDependencyResolver> _resolvers;

    public ExtensionLoadContext(IEnumerable<string> depsJsonPaths)
        : base("ExtensionContext", isCollectible: false)
    {
        _resolvers = depsJsonPaths
            .Where(File.Exists)
            .Select(p => new AssemblyDependencyResolver(p))
            .ToList();
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == null)
            return null;

        // 默认 ALC 已加载同名程序集则复用，避免同一类型两份身份
        foreach (var loaded in AssemblyLoadContext.Default.Assemblies)
        {
            if (string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
                return loaded;
        }

        // 反共享程序集双副本分裂（根治 Method not found / Entry point was not found）：
        // FileDriven 宿主引擎与 Agent 扩展共享几乎整条引擎/UI 栈（WorkFlowMethod/Share.*/Plate.*/HtmlViewer 等）。
        // 若此处按 deps.json 自装，宿主(默认 ALC)与 Agent(scoped ALC)各持一份 → 跨边界类型身份分裂 → 成员签名不相容。
        // 故：凡默认 ALC **可解析**（即 FileDriven 依赖链能装载的共享程序集）一律复用默认副本（单一身份）；
        // 仅 Agent-only / 扩展专属（默认解析不到）才落到本 scoped ALC 自装。
        try
        {
            var byDefault = AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
            if (byDefault != null)
                return byDefault;
        }
        catch (FileNotFoundException) { /* 默认 ALC 无法解析 = Agent-only/扩展专属，落入下方自装 */ }
        catch (BadImageFormatException) { /* 交由下方自装（按 deps.json 定位）兜底 */ }

        foreach (var resolver in _resolvers)
        {
            var path = resolver.ResolveAssemblyToPath(assemblyName);
            if (path != null)
                return LoadFromAssemblyPath(path);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        foreach (var resolver in _resolvers)
        {
            var path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (path != null)
                return LoadUnmanagedDllFromPath(path);
        }

        return IntPtr.Zero;
    }
}