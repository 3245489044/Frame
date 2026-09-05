
using System.Reflection;
using System.Text.Json;
using CommandFramework.Utilities;
using FrontEndDriven.Diagnostics;

namespace FrontEndDriven.Shared.Utils;

public static class PathManager
{

    private static string? _cachedProjectRoot;
    private static PathConfig? _cachedPathConfig;
    private static readonly object _cacheLock = new();


    private sealed class PathConfig
    {
        public string ProjectRoot { get; set; } = "";

        public string FileDrivenRoot { get; set; } = "Projects/FileDriven";

        public string DrivenConfig { get; set; } = "Projects/Information/DrivenConfig";

        public string JsonConfig { get; set; } = "Projects/Information/DrivenConfig/JsonConfig";

        public string Scripts { get; set; } = "Projects/Information/DrivenConfig/Scripts";

        public string Workflow { get; set; } = "Projects/Information/DrivenConfig/WorkFlow";

        public string TempletConfig { get; set; } = "Projects/Information/DrivenConfig/TempletConfig";

        public string Templates { get; set; } = "Projects/Information/DrivenConfig/Templates";

        public string Configs { get; set; } = "Projects/Information/DrivenConfig/Configs";

        public string Usings { get; set; } = "Projects/Information/DrivenConfig/Usings";

        public string LogMessages { get; set; } = "Projects/FileDriven/filedriven_log_messages.json";

        public string LogFilterConfig { get; set; } = "Projects/FileDriven/LogFilterConfig.json";

        public string FrameworkConfig { get; set; } = "Projects/FileDriven/framework.config.json";

        public string BuildOutput { get; set; } = "Projects/FileDriven/BuildOutput";

        public string DiagnosticsConfig { get; set; } = "Projects/FileDriven/DiagnosticsConfig";
    }


    public static string GetProjectRoot()
    {
        if (_cachedProjectRoot != null)
            return _cachedProjectRoot;

        lock (_cacheLock)
        {
            if (_cachedProjectRoot != null)
                return _cachedProjectRoot;

            var config = LoadPathConfigInternal();
            if (!string.IsNullOrEmpty(config?.ProjectRoot) && Directory.Exists(config.ProjectRoot))
            {
                _cachedProjectRoot = config.ProjectRoot;
                return _cachedProjectRoot;
            }

            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (assemblyDir != null)
            {
                var root = FindProjectRootUpward(assemblyDir);
                if (root != null)
                {
                    _cachedProjectRoot = root;
                    return root;
                }
            }

            var cwd = Directory.GetCurrentDirectory();
            var rootFromCwd = FindProjectRootUpward(cwd);
            if (rootFromCwd != null)
            {
                _cachedProjectRoot = rootFromCwd;
                return rootFromCwd;
            }

            // 移除 StackTrace 调用帧兜底（脆弱运行时技巧 + 空 catch 吞异常）——cwd 为最终回退
            _cachedProjectRoot = cwd;
            return cwd;
        }
    }

    public static void ResetProjectRootCache()
    {
        lock (_cacheLock)
        {
            _cachedProjectRoot = null;
            _cachedPathConfig = null;
        }
    }

    public static void ReloadConfig()
    {
        lock (_cacheLock)
        {
            _cachedPathConfig = null;
            _cachedProjectRoot = null;
        }
    }


    public static string GetFileDrivenRoot()
    {
        var config = GetPathConfig();
        if (!string.IsNullOrEmpty(config?.FileDrivenRoot))
            return ResolveAbsolute(config.FileDrivenRoot);

        return Path.Combine(GetProjectRoot(), "FileDriven");
    }

    public static string GetDrivenConfigDir()
    {
        var config = GetPathConfig();
        return ResolveAbsolute(config?.DrivenConfig ?? "FileDriven/DrivenConfig");
    }

    public static string GetJsonConfigDir()
    {
        var config = GetPathConfig();
        return ResolveAbsolute(config?.JsonConfig ?? "FileDriven/DrivenConfig/JsonConfig");
    }

    public static string GetScriptsDir()
    {
        var config = GetPathConfig();
        return ResolveAbsolute(config?.Scripts ?? "FileDriven/DrivenConfig/Scripts");
    }

    public static string GetWorkFlowDir()
    {
        var config = GetPathConfig();
        return ResolveAbsolute(config?.Workflow ?? "FileDriven/DrivenConfig/WorkFlow");
    }

    public static string GetTempletConfigDir()
    {
        var config = GetPathConfig();
        return ResolveAbsolute(config?.TempletConfig ?? "FileDriven/DrivenConfig/TempletConfig");
    }

    public static string GetTemplatesRoot()
    {
        var config = GetPathConfig();
        return ResolveAbsolute(config?.Templates ?? "FileDriven/DrivenConfig/Templates");
    }

    /// <summary>Agent 运行时请求体模板库根：Ui_*Catalog 的约定扫描根。模板按动作分类存放于
    /// AgentRun 子目录，id = 分类/文件名（Templates 根下仅此一个模板库目录，另含 styles.json 样式文件非模板）。</summary>
    public static string GetAgentRunTemplatesRoot()
        => Path.Combine(GetTemplatesRoot(), "AgentRun");

    public static string GetConfigsDir()
    {
        var config = GetPathConfig();
        return ResolveAbsolute(config?.Configs ?? "FileDriven/DrivenConfig/Configs");
    }

    public static string GetUsingsDir()
    {
        var config = GetPathConfig();
        return ResolveAbsolute(config?.Usings ?? "FileDriven/DrivenConfig/Usings");
    }


    public static string GetLogMessagesFilePath()
    {
        var config = GetPathConfig();
        return ResolveAbsolute(config?.LogMessages ?? "FileDriven/filedriven_log_messages.json");
    }

    public static string GetLogFilterConfigPath()
    {
        var config = GetPathConfig();
        return ResolveAbsolute(config?.LogFilterConfig ?? "FileDriven/LogFilterConfig.json");
    }


    public static string GetBuildOutputRoot()
    {
        var config = GetPathConfig();
        return ResolveAbsolute(config?.BuildOutput ?? "FileDriven/BuildOutput");
    }

    public static string GetProjectBuildOutput(string projectName, string configuration = "Debug")
    {
        return Path.Combine(GetBuildOutputRoot(), configuration, projectName);
    }

    public static string GetLauncherOutputDir()
    {
        var release = GetProjectBuildOutput("FileDriven.Launcher", "Release");
        var debug = GetProjectBuildOutput("FileDriven.Launcher", "Debug");

        if (File.Exists(Path.Combine(release, "FileDriven.Launcher.exe")))
            return release;

        return debug;
    }

    public static string GetLauncherExePath()
    {
        return Path.Combine(GetLauncherOutputDir(), "FileDriven.Launcher.exe");
    }


    public static string GetAbstractionsDir() =>
        Path.Combine(GetFileDrivenRoot(), "FileDriven.Abstractions");

    public static string GetBusinessDir() =>
        Path.Combine(GetFileDrivenRoot(), "FileDriven.Business");

    public static string GetExecutionDir() =>
        Path.Combine(GetFileDrivenRoot(), "FileDriven.Execution");

    public static string GetJsonReferDir() =>
        Path.Combine(GetFileDrivenRoot(), "FileDriven.JsonRefer");

    public static string GetLauncherDir() =>
        Path.Combine(GetFileDrivenRoot(), "FileDriven.Launcher");

    public static string GetLoggingDir() =>
        Path.Combine(GetFileDrivenRoot(), "FileDriven.Logging");

    public static string GetPipelineDir() =>
        Path.Combine(GetFileDrivenRoot(), "FileDriven.Pipeline");

    public static string GetUIDir() =>
        Path.Combine(GetFileDrivenRoot(), "FileDriven.UI");

    public static string GetUtilitiesDir() =>
        Path.Combine(GetFileDrivenRoot(), "FileDriven.Utilities");


    public static string GetFrameworkConfigPath()
    {
        var config = GetPathConfig();
        return ResolveAbsolute(config?.FrameworkConfig ?? "FileDriven/framework.config.json");
    }


    public static string GetDiagnosticsConfigDir()
    {
        var config = GetPathConfig();
        return ResolveAbsolute(config?.DiagnosticsConfig ?? "FileDriven/DiagnosticsConfig");
    }

    public static string GetCategoryAliasesPath()
    {
        return Path.Combine(GetDiagnosticsConfigDir(), "category-aliases.json");
    }

    public static string GetLogCategoriesPath()
    {
        return Path.Combine(GetDiagnosticsConfigDir(), "log-categories.json");
    }

    public static string GetDiagnosticsSettingsPath()
    {
        return Path.Combine(GetDiagnosticsConfigDir(), "diagnostics-settings.json");
    }


    public static string GetFrontEndDrivenLogMessagesPath()
    {
        return Path.Combine(GetProjectRoot(), "CsharpPrj", "Operation", "FrontEndDriven", "fde_log_messages.json");
    }


    private static PathConfig? GetPathConfig()
    {
        if (_cachedPathConfig != null)
            return _cachedPathConfig;

        return LoadPathConfigInternal();
    }

    private static PathConfig? LoadPathConfigInternal()
    {
        try
        {
            var configPath = FindFrameworkConfigPath();
            if (configPath == null || !File.Exists(configPath))
                return null;

            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var pathsElement = doc.RootElement.TryGetProperty("paths", out var p) ? p : default;

            if (pathsElement.ValueKind != JsonValueKind.Object)
                return null;

            var config = new PathConfig();

            // 表驱动：paths 元素键 → PathConfig 字段 setter（消除 13 段重复 if TryGetProperty 复制粘贴）
            var pathSetters = new (string Key, Action<string> Setter)[]
            {
                ("projectRoot", v => config.ProjectRoot = v),
                ("fileDrivenRoot", v => config.FileDrivenRoot = v),
                ("drivenConfig", v => config.DrivenConfig = v),
                ("jsonConfig", v => config.JsonConfig = v),
                ("scripts", v => config.Scripts = v),
                ("workflow", v => config.Workflow = v),
                ("templetConfig", v => config.TempletConfig = v),
                ("templates", v => config.Templates = v),
                ("configs", v => config.Configs = v),
                ("usings", v => config.Usings = v),
                ("logMessages", v => config.LogMessages = v),
                ("logFilterConfig", v => config.LogFilterConfig = v),
                ("frameworkConfig", v => config.FrameworkConfig = v),
                ("buildOutput", v => config.BuildOutput = v),
                ("diagnosticsConfig", v => config.DiagnosticsConfig = v)
            };

            foreach (var (key, setter) in pathSetters)
            {
                // JsonValueKind.String 的 GetString() 恒非 null，直接覆写（等价旧逐字段语义）
                if (pathsElement.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
                    setter(prop.GetString() ?? string.Empty);
            }

            _cachedPathConfig = config;
            return config;
        }
        catch (Exception ex)
        {
            // R5 修复：空 catch 显式化——配置解析失败仅记日志并返回 null（调用方回退默认路径）
            FrontEndDrivenLog.Log("PathManager_LoadConfig_failed",
                new { error = ex.Message, exceptionType = ex.GetType().Name });
            return null;
        }
    }

    private static string? FindFrameworkConfigPath()
    {
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (assemblyDir != null)
        {
            // 新 Main 拓扑：framework.config.json 现位于 Main\Projects\FileDriven（FileDriven 拆分进 Projects 子模块，
            // 从 FrontEndDriven 所在 Frame 子树向上回溯找不到，故显式按 Main 根定位）。
            var mainRoot = FindMainRepoRoot(assemblyDir);
            if (mainRoot != null)
            {
                var inRepo = Path.Combine(mainRoot, "Projects", "FileDriven", "framework.config.json");
                if (File.Exists(inRepo)) return inRepo;
            }

            var found = FindFileUpward(assemblyDir, "framework.config.json");
            if (found != null) return found;
        }

        var cwd = Directory.GetCurrentDirectory();
        var fromCwd = FindFileUpward(cwd, "framework.config.json");
        if (fromCwd != null) return fromCwd;

        return null;
    }

    private static string ResolveAbsolute(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            return relativePath;

        return Path.GetFullPath(Path.Combine(GetProjectRoot(), relativePath));
    }

    private static string? FindFileUpward(string startPath, string fileName)
    {
        if (string.IsNullOrEmpty(startPath)) return null;

        var current = startPath;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, fileName);
            if (File.Exists(candidate))
                return candidate;

            var parent = Path.GetDirectoryName(current);
            if (parent == current || parent == null) break;
            current = parent;
        }

        return null;
    }

    private static string? FindProjectRootUpward(string startPath)
    {
        // 优先 Main 仓库根（统一包含 Projects/Frame 的目录 = 各相对路径单一基准；子模块就近 .git 会造成 Frame 误判）
        var main = FindMainRepoRoot(startPath);
        if (main != null)
            return main;

        // 旧布局/单仓兜底：就近 git 根（遗留，防布局回退回归）
        return ProjectRootLocator.FindRootUpward(startPath);
    }

    /// <summary>自 startPath 向上，返回首个同时含 Projects 与 Frame\CsharpPrj 的目录（= Main 仓库根）；未找到返回 null。
    /// 用目录特征而非 .git 锚定，避免 Frame/Projects 子模块就近命中造成根漂移。</summary>
    private static string? FindMainRepoRoot(string startPath)
    {
        if (string.IsNullOrEmpty(startPath)) return null;
        var current = startPath;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "Projects"))
                && Directory.Exists(Path.Combine(current, "Frame", "CsharpPrj")))
                return current;

            var parent = Directory.GetParent(current)?.FullName;
            if (parent == null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }
        return null;
    }
}
