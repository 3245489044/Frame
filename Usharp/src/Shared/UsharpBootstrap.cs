using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Usharp.Bootstrap;

/// <summary>
/// Usharp 烘焙轨运行期引导（2026-11-08）：Usharp 对 CsharpPrj 全面转「权威烘焙二进制引用」后，
/// 输出目录不再持有上游 dll —— 模块装载期注册 <c>AssemblyLoadContext.Default.Resolving</c>，
/// 从权威目录就地解析缺失程序集。只依赖 System，绝不触碰共享类型
/// （否则探针注册晚于共享类型加载即失效）。
/// <para>
/// 编译期以 Link 方式同时进入 Usharp.Cli 与 Usharp.Bundle 两个 Exe；
/// 库形态消费方（Agent/Produce 等）由宿主自己的 LauncherBootstrap 承担，不依赖本文件。
/// </para>
/// </summary>
internal static class UsharpBootstrap
{
    private static readonly Lazy<string?> RepoRoot = new(FindRepoRoot);

    /// <summary>构建配置（Debug/Release），由 AppContext.BaseDirectory 推断。</summary>
    internal static string ConfigName { get; } = DetectConfigName();

    /// <summary>核心域权威目录：Frame\CsharpPrj\BuildOutput\$(Config)（无 TFM 层）。</summary>
    internal static string? CsharpCoreDir => Rooted("CsharpPrj");

    /// <summary>平台库权威目录：Frame\DataCenter\BuildOutput\$(Config)。</summary>
    internal static string? DataCenterDir => Rooted("DataCenter");

    /// <summary>本区权威目录：Frame\Usharp\BuildOutput\$(Config)（薄输出下兄弟程序集就地解析）。</summary>
    internal static string? UsharpDir => Rooted("Usharp");

    private static string? Rooted(string area)
    {
        var repo = RepoRoot.Value;
        return repo is null ? null : Path.Combine(repo, "Frame", area, "BuildOutput", ConfigName);
    }

    /// <summary>模块装载期注册权威目录探针（早于 Main 的 JIT，保证任何共享类型加载前探针已就位）。
    /// 解析顺序：核心域（CsharpPrj 全族）→ 平台库（DataCenter）→ 本区（Usharp）。</summary>
    [ModuleInitializer]
    internal static void InitModule()
    {
        var probes = new[] { CsharpCoreDir, DataCenterDir, UsharpDir };
        if (!probes.Any(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)))
            return; // 权威目录全缺 → 不注册（后续按既有缺失降级路径走）

        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            var simple = name?.Name;
            if (string.IsNullOrEmpty(simple))
                return null;

            foreach (var dir in probes)
            {
                if (string.IsNullOrEmpty(dir))
                    continue;
                var candidate = Path.Combine(dir, simple + ".dll");
                if (File.Exists(candidate))
                    return ctx.LoadFromAssemblyPath(candidate);
            }

            return null;
        };
    }

    private static string DetectConfigName()
    {
        var baseDir = AppContext.BaseDirectory ?? string.Empty;
        return baseDir.Contains("\\Release\\", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
    }

    /// <summary>仓库根锚：目录同时含 Projects（产品/资源）与 Frame\CsharpPrj（平台），兼容旧布局（顶层 CsharpPrj）。</summary>
    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Projects"))
                && Directory.Exists(Path.Combine(dir.FullName, "Frame", "CsharpPrj")))
                return dir.FullName;

            if (Directory.Exists(Path.Combine(dir.FullName, "CsharpPrj"))
                && File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
                return dir.FullName;

            dir = dir.Parent;
        }
        return null;
    }
}
