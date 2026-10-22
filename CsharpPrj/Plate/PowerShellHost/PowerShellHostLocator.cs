using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Plate.PowerShellHost;

/// <summary>
/// PowerShell 宿主可执行定位（全仓单一实现）：显式配置 → 环境变量 → Store 包族 → 常规安装位 → PATH。
/// <para>
/// 可用性判据 = 结构事实（存在 ∧ 非指向空目标的重解析点 ∧ 文件长度 &gt; 0）。Store 的 App Execution Alias
/// 与包族目录内的 <c>pwsh.exe</c> 均可能是 0 字节 reparse point，<see cref="File.Exists(string)"/> 无法区分，
/// 故必须先解链再判长度；本类是 <c>WindowsApps</c> 语义的唯一判定点。
/// </para>
/// <para>
/// 失败只抛 <see cref="PowerShellHostNotFoundException"/>（稳定码 + ≤300 字符短消息）；完整候选清单经
/// <paramref name="detailSink"/> 交给调用方写一次 Debug，不进入正常事件载荷。
/// </para>
/// </summary>
public static class PowerShellHostLocator
{
    /// <summary>稳定失败码（跨层分类器消费；禁以中文文案归因）。</summary>
    public const string ExecutableNotFoundCode = "psruntime.executable_not_found";

    /// <summary>短消息上限（人读；全量候选走 detail）。</summary>
    public const int ShortMessageLimit = 300;

    /// <summary>单个 Store 根的有界枚举上限（禁无界目录扫描）。</summary>
    public const int MaxCandidatesPerRoot = 8;

    private static readonly Regex PackageVersionPattern = new(
        @"^Microsoft\.PowerShell_(\d+(?:\.\d+){1,3})_",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>解析 pwsh 绝对路径；未命中抛 <see cref="PowerShellHostNotFoundException"/>。</summary>
    /// <param name="configuredPath">显式配置（ps1-runtime.json / 调用方）；空则跳过。</param>
    /// <param name="allowWindowsPowerShell51">是否允许显式回落到 Windows PowerShell 5.1（默认禁用）。</param>
    /// <param name="detailSink">完整候选清单出口（调用方写 Debug；null = 丢弃）。</param>
    public static string Locate(
        string? configuredPath = null,
        bool allowWindowsPowerShell51 = false,
        Action<string>? detailSink = null)
    {
        var candidates = BuildCandidates(configuredPath, allowWindowsPowerShell51);

        // 第一轮：真实文件（长度 > 0 / 可解链到真实文件）优先。
        foreach (var candidate in candidates)
        {
            if (TryResolveUsable(candidate, out var resolved))
                return resolved;
        }

        // 第二轮：Store App Execution Alias 兜底（0 字节 reparse，但 Process.Start 可启动；
        // 实测本机包族别名可正常起 pwsh）。真实文件不存在时才接受。
        foreach (var candidate in candidates)
        {
            if (TryResolveAppExecutionAlias(candidate, out var alias))
                return alias;
        }

        var distinct = candidates
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        detailSink?.Invoke("PowerShellHostLocator 未命中；候选清单：" + string.Join(" | ", distinct));
        throw new PowerShellHostNotFoundException(distinct);
    }

    /// <summary>候选序列（纯函数；顺序即优先级，供单测与诊断复算）。</summary>
    public static IReadOnlyList<string> BuildCandidates(
        string? configuredPath = null,
        bool allowWindowsPowerShell51 = false)
    {
        var candidates = new List<string>();
        AddIfPresent(candidates, configuredPath);
        AddIfPresent(candidates, Environment.GetEnvironmentVariable("PSRUNTIME_PWSH"));

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        // 别名根（0 字节 reparse point；保留为候选，由 TryResolveUsable 结构判据拒绝）。
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            AddIfPresent(candidates, Path.Combine(localAppData, "Microsoft", "WindowsApps", "pwsh.exe"));
            AddStorePackageCandidates(candidates, Path.Combine(localAppData, "Microsoft", "WindowsApps"));
        }
        if (!string.IsNullOrWhiteSpace(programFiles))
            AddStorePackageCandidates(candidates, Path.Combine(programFiles, "WindowsApps"));

        if (!string.IsNullOrWhiteSpace(programFiles))
            AddIfPresent(candidates, Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe"));
        if (!string.IsNullOrWhiteSpace(programFilesX86))
            AddIfPresent(candidates, Path.Combine(programFilesX86, "PowerShell", "7", "pwsh.exe"));

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = directory.Trim().Trim('"');
            if (dir.Length == 0) continue;

            // 仅探 .exe：Windows 下裸名在 Process.Start(UseShellExecute=false) 语境不构成可执行候选。
            AddIfPresent(candidates, Path.Combine(dir, "pwsh.exe"));
        }

        if (allowWindowsPowerShell51)
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var dir = directory.Trim().Trim('"');
                if (dir.Length == 0) continue;
                AddIfPresent(candidates, Path.Combine(dir, "powershell.exe"));
            }

            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrWhiteSpace(system32))
                AddIfPresent(candidates, Path.Combine(system32, "WindowsPowerShell", "v1.0", "powershell.exe"));
        }

        return candidates;
    }

    /// <summary>
    /// 候选可用性（结构判据唯一实现）：存在 → 解链（reparse）→ 长度 &gt; 0。
    /// </summary>
    public static bool TryResolveUsable(string? candidate, out string resolved)
    {
        resolved = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        try
        {
            if (!File.Exists(candidate)) return false;

            var full = Path.GetFullPath(candidate);
            var info = new FileInfo(full);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                var target = File.ResolveLinkTarget(full, returnFinalTarget: true);
                if (target is null || !target.Exists) return false;
                full = target.FullName;
                info = new FileInfo(full);
            }

            if (info.Length <= 0) return false;

            resolved = full;
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    /// <summary>
    /// Store App Execution Alias 判定（WindowsApps 语义唯一处）：0 字节 reparse 且位于
    /// <c>%LOCALAPPDATA%\Microsoft\WindowsApps</c> 之下。仅作真实文件缺失时的兜底候选。
    /// </summary>
    public static bool TryResolveAppExecutionAlias(string? candidate, out string resolved)
    {
        resolved = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        try
        {
            if (!File.Exists(candidate)) return false;

            var full = Path.GetFullPath(candidate);
            var info = new FileInfo(full);
            if (!info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.Length > 0)
                return false;

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData)) return false;

            var aliasRoot = Path.TrimEndingDirectorySeparator(
                Path.Combine(localAppData, "Microsoft", "WindowsApps"));
            if (!full.StartsWith(aliasRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;

            resolved = full;
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    private static void AddStorePackageCandidates(List<string> candidates, string storeRoot)
    {
        if (string.IsNullOrWhiteSpace(storeRoot) || !Directory.Exists(storeRoot)) return;

        string[] packages;
        try
        {
            packages = Directory.GetDirectories(storeRoot, "Microsoft.PowerShell_*", SearchOption.TopDirectoryOnly);
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        catch (ArgumentException) { return; }

        foreach (var package in packages
                     .OrderByDescending(PackageVersion)
                     .ThenBy(static p => p, StringComparer.OrdinalIgnoreCase)
                     .Take(MaxCandidatesPerRoot))
        {
            AddIfPresent(candidates, Path.Combine(package, "pwsh.exe"));
        }
    }

    private static Version PackageVersion(string directory)
    {
        var match = PackageVersionPattern.Match(Path.GetFileName(directory));
        return match.Success && Version.TryParse(match.Groups[1].Value, out var version)
            ? version
            : new Version(0, 0);
    }

    private static void AddIfPresent(List<string> candidates, string? candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
            candidates.Add(candidate);
    }
}

/// <summary>pwsh 定位失败（稳定码 + 短消息 + 全量候选仅内存/诊断面）。</summary>
public sealed class PowerShellHostNotFoundException : InvalidOperationException
{
    public PowerShellHostNotFoundException(IReadOnlyList<string> candidates)
        : base(BuildMessage(candidates))
    {
        Candidates = candidates ?? Array.Empty<string>();
    }

    public string Code => PowerShellHostLocator.ExecutableNotFoundCode;

    public IReadOnlyList<string> Candidates { get; }

    private static string BuildMessage(IReadOnlyList<string> candidates)
    {
        var count = candidates?.Count ?? 0;
        var samples = (candidates ?? Array.Empty<string>())
            .Take(3)
            .Select(static c => c.Length <= 60 ? c : c[..60] + "…")
            .ToArray();
        var sampleText = samples.Length == 0 ? "（无）" : string.Join("; ", samples);
        var more = count > samples.Length ? $"（+{count - samples.Length}）" : string.Empty;
        var message = $"未找到 pwsh（{PowerShellHostLocator.ExecutableNotFoundCode}）。候选 {count} 个；示例：{sampleText}{more}";
        return message.Length <= PowerShellHostLocator.ShortMessageLimit
            ? message
            : message[..PowerShellHostLocator.ShortMessageLimit] + "…";
    }
}
