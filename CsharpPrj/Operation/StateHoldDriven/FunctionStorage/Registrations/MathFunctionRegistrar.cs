
using System.Reflection;
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
        var engine = new AutoRegisterEngine(
            resolveAssembly: path =>
            {
                if (Path.IsPathRooted(path))
                    return Assembly.LoadFrom(path);

                var configDirPath = Path.GetFullPath(Path.Combine(configDir, path));
                if (File.Exists(configDirPath))
                    return Assembly.LoadFrom(configDirPath);

                var baseDirPath = Path.GetFullPath(Path.Combine(baseDir, path));
                if (File.Exists(baseDirPath))
                    return Assembly.LoadFrom(baseDirPath);

                // ★ 薄启动层（FileDriven 不持有上游 dll）：configDir/baseDir 均缺时，交默认 ALC 的
                //   Resolving 探针从 CsharpPrj\BuildOutput\$(Config) 权威目录原位解析（LauncherBootstrap 注册）。
                //   探针缺位时回退显式 LoadFrom，保持既有诊断语义。
                var simpleName = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(simpleName))
                {
                    try { return Assembly.Load(new AssemblyName(simpleName)); }
                    catch (FileNotFoundException) { /* 探针未命中 → 回退 LoadFrom */ }
                }

                return Assembly.LoadFrom(Path.GetFullPath(path));
            });
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

        try
        {
            var projectRoot = PathManager.ProjectPathManager.FindProjectRoot();
            if (!string.IsNullOrEmpty(projectRoot))
            {
                var projectPath = Path.Combine(projectRoot, "CsharpPrj", "Operation", "StateHoldDriven",
                    "FunctionStorage", "Registrations", "math-register-config.json");
                if (File.Exists(projectPath))
                    return projectPath;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"解析项目根失败，无法定位 math-register-config.json：{ex.Message}", ex);
        }

        throw new FileNotFoundException(
            "未找到数学函数注册配置 math-register-config.json，期望位置：" +
            "CsharpPrj/Operation/StateHoldDriven/FunctionStorage/Registrations/");
    }
}
