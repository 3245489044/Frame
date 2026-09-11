using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Modules.Registry;

/// <summary>
/// 扩展代数宿主（S4-1 / G7.2）—— <b>一个代数 = 一个可收集 scoped ALC + 其装配清单</b>。
/// 取代原 <c>static ExtensionAssemblyHost</c>：运行状态不再由静态类持有，而随代数实例创建/释放，
/// 使换代（热更新带数据）后旧 ALC 具备被 GC 回收的能力（跨 ALC 类型失效面收敛）。
/// <para>
/// 装载语义（与原实现一致）：扩展层（Agent.* / GameEditor.* / PrjBridge.dll 等）不在 FileDriven 的 deps.json，
/// 由各产品经 DeploySelfToRuntime 聚合输出整体部署到运行目录，并携带各自的 deps.json（扩展闭包清单）；
/// 启动期用 AssemblyDependencyResolver 定位这些清单，建专属 scoped ALC 按 .NET 原生语义装载
/// （传递依赖/版本/native 均由平台处理）。缺失清单时保持空 ALC，降级（契合 TryRegister* 约定）。
/// </para>
/// <para>
/// 诚实边界：可收集（<c>isCollectible: true</c>）≠ 必然回收 —— 默认 ALC 侧若长期持有扩展类型
/// （反射注册的回调/委托/句柄），旧代数仍会存活；故换代须 Drain + **不跨代数复用句柄/委托**（文档 D5）。
/// 本类只保证「代数实例化 + ALC 可收集」。
/// </para>
/// 权威: Frame\DataCenter\Doc\DataCenter-Agent运行时数据接入-设计与实施.md §4（S4-1）、
///      DataCenter-通用化重构与实施计划.md G7.2。
/// </summary>
public sealed class ExtensionGenerationHost
{
    private const string DefaultManifest = "PrjBridge.deps.json";
    private const string IndexFileName = "ExtensionLoaderConfig.json";

    private readonly ExtensionLoadContext? _context;

    /// <summary>创建一代扩展宿主（装配清单解析：显式路径 → 索引文件 → 默认回退；全缺 = 降级空 ALC）。</summary>
    /// <param name="generation">代数号（进程级单调序列；0 = 首个代数）。</param>
    /// <param name="depsJsonPaths">显式 deps.json 清单（按名可相对运行目录）；空 = 按索引文件/默认回退解析。</param>
    public ExtensionGenerationHost(int generation, IEnumerable<string> depsJsonPaths)
    {
        ArgumentNullException.ThrowIfNull(depsJsonPaths);

        Generation = generation;
        Manifests = ResolveManifests(depsJsonPaths.ToArray());
        // 未部署 → 降级（无 scoped ALC；消费方带 null 护栏）
        _context = Manifests.Count > 0 ? new ExtensionLoadContext(Manifests, $"ExtensionContext#{generation}") : null;
    }

    /// <summary>代数号（进程级单调序列）。</summary>
    public int Generation { get; }

    /// <summary>本代数装载的全部扩展 deps.json 清单（绝对路径；未部署时为空）。</summary>
    public IReadOnlyList<string> Manifests { get; }

    /// <summary>主扩展 deps.json（首个清单；未部署时为空串）。兼容旧取用方。</summary>
    public string DepsJsonPath => Manifests.Count > 0 ? Manifests[0] : "";

    /// <summary>本代数是否已装载 scoped ALC（false = 未部署降级）。</summary>
    public bool IsLoaded => _context is not null;

    /// <summary>
    /// 装载上下文弱引用（<b>换代回收判据</b>：<see cref="Unload"/> 且外部引用释放后 <c>IsAlive == false</c>；
    /// 未装载时为 null）。
    /// </summary>
    public WeakReference? ContextReference => _context is null ? null : new WeakReference(_context);

    /// <summary>按程序集限定名在本代数的 scoped ALC 里解析类型（显式发现，不撞 Type.GetType 运气）。</summary>
    public Type? ResolveType(string typeStr)
    {
        if (_context == null)
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
            var asm = _context.LoadFromAssemblyName(new AssemblyName(asmName));
            return asm.GetType(fullTypeName, throwOnError: false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>按程序集简单名在本代数的 scoped ALC 里加载（供 PrjBridgeWiringHook 等反射装载用）。</summary>
    public Assembly? Load(string simpleName)
    {
        if (_context == null)
            return null;

        try
        {
            return _context.LoadFromAssemblyName(new AssemblyName(simpleName));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 部署校验：解析本代数全部扩展 deps.json 的 runtime 资产，报告磁盘缺失项（快速定位缺哪个依赖）。
    /// 仅诊断用，不抛异常。
    /// </summary>
    public IReadOnlyList<string> ValidateDeployment()
    {
        var missing = new List<string>();
        if (Manifests.Count == 0)
            return missing;

        foreach (var depsPath in Manifests)
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

    /// <summary>
    /// 卸载本代数的 scoped ALC（换代调用：Drain 后、确认无跨代引用时调用）。
    /// 卸载不阻塞 GC；回收由运行时决定（判据见 <see cref="ContextReference"/>）。
    /// </summary>
    public void Unload() => _context?.Unload();

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
}

/// <summary>
/// 扩展代数工厂（S4-1）—— 进程级代数序列 + <b>当前代数的唯一持有者</b>。
/// 业务侧经本工厂取当前代数（<b>不自行持有</b> ALC/代数实例，否则旧代数无法回收）；
/// 换代 = <see cref="Swap"/>（新代数就位、返回旧代数）→ 调用方 Drain 后 <see cref="ExtensionGenerationHost.Unload"/>。
/// <para>
/// 迁移说明：本类取代原 <c>static ExtensionAssemblyHost</c> 的全部静态面 ——
/// <see cref="Initialize"/>（启动期注册清单，幂等首调生效）、<see cref="ResolveType"/>/<see cref="Load"/>
/// （装载期解析；未初始化时惰性初始化，语义同原实现）、<see cref="ValidateDeployment"/>（诊断）。
/// </para>
/// </summary>
public static class ExtensionGenerationFactory
{
    private static readonly object Gate = new();
    private static ExtensionGenerationHost? _current;
    private static int _generation;
    private static bool _initialized;

    /// <summary>当前代数（未初始化 → 惰性按默认清单来源创建首个代数；线程安全）。</summary>
    public static ExtensionGenerationHost Current
    {
        get
        {
            lock (Gate)
            {
                if (!_initialized)
                {
                    _current = new ExtensionGenerationHost(_generation, Array.Empty<string>());
                    _initialized = true;
                }
                return _current!;
            }
        }
    }

    /// <summary>当前代数号（0 = 首个代数）。</summary>
    public static int Generation { get { lock (Gate) return _generation; } }

    /// <summary>是否已创建首个代数。</summary>
    public static bool IsInitialized { get { lock (Gate) return _initialized; } }

    /// <summary>启动期创建首个代数（幂等：首调生效，与原 <c>Initialize</c> 语义一致）。</summary>
    public static ExtensionGenerationHost Initialize(params string[] depsJsonPaths)
    {
        ArgumentNullException.ThrowIfNull(depsJsonPaths);

        lock (Gate)
        {
            if (!_initialized)
            {
                _current = new ExtensionGenerationHost(_generation, depsJsonPaths);
                _initialized = true;
            }
            return _current!;
        }
    }

    /// <summary>
    /// 换代：创建 <c>generation+1</c> 的新代数并交换当前指向；返回旧代数（调用方 Drain 后 Unload）。
    /// 失败语义：新代数构造异常 → 当前指向不变（旧代数继续可用，不半迁）。
    /// </summary>
    public static ExtensionGenerationHost? Swap(params string[] depsJsonPaths)
    {
        ArgumentNullException.ThrowIfNull(depsJsonPaths);

        lock (Gate)
        {
            var next = new ExtensionGenerationHost(_generation + 1, depsJsonPaths);
            var previous = _current;
            _current = next;
            _generation = next.Generation;
            _initialized = true;
            return previous;
        }
    }

    /// <summary>测试隔离：清空当前代数与序列（不 Unload，便于断言旧代数回收）。</summary>
    public static void ResetForTesting()
    {
        lock (Gate)
        {
            _current = null;
            _generation = 0;
            _initialized = false;
        }
    }

    /// <summary>按程序集限定名在当前代数解析类型（未部署 → null）。</summary>
    public static Type? ResolveType(string typeStr) => Current.ResolveType(typeStr);

    /// <summary>按程序集简单名在当前代数加载（未部署 → null）。</summary>
    public static Assembly? Load(string simpleName) => Current.Load(simpleName);

    /// <summary>部署校验（当前代数；未装载 = 空清单）。</summary>
    public static IReadOnlyList<string> ValidateDeployment() => Current.ValidateDeployment();
}

/// <summary>
/// 专属扩展装载上下文：以多个 AssemblyDependencyResolver（一个清单一个）解析。<b>可收集</b>（S4-1：换代回收前提）。
/// Load 覆写先复用默认 ALC 已加载的同名程序集（防 FileDriven.Abstractions 等类型身份分裂），
/// 再按清单顺序 fallback 定位 DLL；所有清单找不到返回 null → 交还默认 ALC。
/// </summary>
public sealed class ExtensionLoadContext : AssemblyLoadContext
{
    private readonly List<AssemblyDependencyResolver> _resolvers;

    /// <param name="depsJsonPaths">扩展 deps.json 清单集合。</param>
    /// <param name="name">ALC 名称（诊断用；默认 "ExtensionContext"，代数宿主按代数号命名）。</param>
    public ExtensionLoadContext(IEnumerable<string> depsJsonPaths, string name = "ExtensionContext")
        : base(name, isCollectible: true)
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
