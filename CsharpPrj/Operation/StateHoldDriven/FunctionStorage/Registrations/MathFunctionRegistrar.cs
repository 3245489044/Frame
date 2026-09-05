
using System.Text.Json;
using Modules.Registry;
using TConfig;
using Modules.Registry.AutoRegister;

namespace FunctionStorage.Registrations;

[FunctionLibrary]
public static class MathFunctionRegistrar
{
    public static void ResetRegistration()
    {
    }

    public static void RegisterAll()
    {
        RegisterAll(FunctionStorageRegistry.Registry);
    }

    public static void RegisterAll(ITrConfigRegistry registry)
    {
        RegisterAll(registry, configPath: null);
    }

    public static void RegisterAll(ITrConfigRegistry registry, string? configPath)
    {
        // 配置缺失即抛错（FindConfigPath 不再返回 null）：杜绝静默空注册掩盖扩展数学函数缺失。
        configPath ??= FindConfigPath();
        FsLog.Log("MathFunctionRegistrar_FindConfigPath_result", new
        {
            configPath,
            baseDir = AppDomain.CurrentDomain.BaseDirectory,
            currentDir = Directory.GetCurrentDirectory()
        });

        var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // ★ 通用程序集源装载（收敛 baseDir/CWD 就近猜）：configDir → 运行目录 → 注入权威目录 → 默认 ALC 按名 → 当前目录。
        //   由 Route 布局 catalog 派生的权威产物根（如 CsharpProjectRoot\BuildOutput\Debug）经
        //   Modules.Registry.AutoRegister.AssemblySourceProvider 注入，Frame 不反向依赖 Route。
        var locator = AssemblySourceLocator.CreateForDirectory(configDir);
        var engine = new AutoRegisterEngine(resolveAssembly: locator.Resolve);
        var results = engine.ProcessConfig(configPath, registry);

        // ★ 启动期自检：AutoRegister 完成后对照期望函数清单校验，缺失即启动时显式报错
        //   （把"执行期未找到"提前到启动期，并给出 DLL 版本过旧/未同步的明确诊断）
        ValidateManifest(registry, Path.Combine(configDir, "math-register-manifest.json"), configDir, baseDir);

        int totalSuccess = 0;
        int totalFail = 0;
        foreach (var (entry, result) in results)
        {
            totalSuccess += result.SuccessCount;
            totalFail += result.FailCount;
        }

        FsLog.Log("MathFunctionRegistrar_RegisterAll_result", new
        {
            configPath,
            successCount = totalSuccess,
            failCount = totalFail
        });
    }

    /// <summary>
    /// 启动期注册清单校验：确保 AutoRegister 后期望的数学函数均已注册。
    /// 缺失即在启动时抛 InvalidOperationException（不被 FunctionLibraryLoader 吞掉），
    /// 附带缺失清单 + 加载的 Mathematics.dll 路径/时间戳，提示重新编译并同步。
    /// </summary>
    private static void ValidateManifest(
        ITrConfigRegistry registry,
        string manifestPath,
        string configDir,
        string baseDir)
    {
        if (!File.Exists(manifestPath))
        {
            // 无清单则跳过（向后兼容，不阻断启动）
            FsLog.Log("MathFunctionRegistrar_manifest_missing", new { manifestPath });
            return;
        }

        var expected = LoadManifestExpected(manifestPath);
        if (expected.Count == 0)
            return;

        var missing = expected.Where(name => !registry.Contains(name)).ToList();
        if (missing.Count == 0)
            return;

        var dllInfo = DescribeMathematicsAssembly(configDir, baseDir);
        throw new InvalidOperationException(
            "数学函数启动自检失败：期望注册 " + expected.Count + " 个函数，实际缺失 " + missing.Count + " 个：" +
            "[" + string.Join(", ", missing) + "]。\n" +
            dllInfo + "\n" +
            "可能原因：运行时加载的 Mathematics.dll 版本过旧或未同步（AutoRegister 从编译产物反射注册，源码新增函数需重新编译并同步运行目录）。\n" +
            "修复：重新编译 Mathematics（或整个解决方案），确认运行目录下的 Mathematics.dll 已更新后再次启动。");
    }

    private static List<string> LoadManifestExpected(string manifestPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!doc.RootElement.TryGetProperty("expected", out var arr))
                return new List<string>();

            return arr.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Cast<string>()
                .ToList();
        }
        catch (Exception ex)
        {
            // 清单解析失败不阻断启动，仅告警
            FsLog.Log("MathFunctionRegistrar_manifest_parse_failed", new { manifestPath, error = ex.Message });
            return new List<string>();
        }
    }

    private static string DescribeMathematicsAssembly(string configDir, string baseDir)
    {
        var candidates = new[] { Path.Combine(configDir, "Mathematics.dll"), Path.Combine(baseDir, "Mathematics.dll") };
        foreach (var p in candidates)
        {
            if (File.Exists(p))
            {
                var fi = new FileInfo(p);
                return $"已定位 Mathematics.dll：{p}（最后写入 {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}，大小 {fi.Length} 字节）";
            }
        }

        return $"未在运行目录找到 Mathematics.dll（已检查：{string.Join(" 或 ", candidates)}）";
    }

    private static string FindConfigPath()
    {
        const string marker = "math-register-config.json";
        string[] rel =
        {
            "Operation", "StateHoldDriven", "FunctionStorage", "Registrations", marker
        };

        var searcher = new LayoutArtifactSearcher();

        // ① 注入根派生：产品组合根把 Route 布局 catalog 的 CsharpProjectRoot（Frame\CsharpPrj）作为
        //    LayoutRoot 注入，相对布局段据此派生 —— 不再硬编码 "projectRoot\CsharpPrj\..." 绝对段（收敛 D2/D4）。
        var fromRoots = searcher.FindUnderRoots(rel);
        if (fromRoots != null)
            return fromRoots;

        // ② 向上容器回溯：兼容未注入根 / 非 Main 拓扑（保留旧就近语义；容器名 "CsharpPrj" 为布局约定）。
        var fromUp = searcher.FindUpward(AppDomain.CurrentDomain.BaseDirectory, "CsharpPrj", rel);
        if (fromUp != null)
            return fromUp;

        throw new FileNotFoundException(
            "未找到数学函数注册配置 math-register-config.json。期望经产品组合根注入 Route 布局的 " +
            "CsharpProjectRoot 作为 LayoutRoot 后命中 CsharpProjectRoot\\Operation\\StateHoldDriven\\FunctionStorage\\" +
            "Registrations\\math-register-config.json，或在仓库布局未变更前提下由向上容器回溯命中。\n" +
            $"运行上下文：BaseDirectory={AppDomain.CurrentDomain.BaseDirectory}；CWD={Directory.GetCurrentDirectory()}；" +
            $"注入 LayoutRoots=[{string.Join(" ; ", new LayoutArtifactSearcher().LayoutRoots)}]；" +
            $"注入 AssemblySearchDirectories=[{string.Join(" ; ", AssemblySourceProvider.Current.AssemblySearchDirectories)}]");
    }

}

