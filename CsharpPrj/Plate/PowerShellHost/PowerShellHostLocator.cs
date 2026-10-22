using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

    /// <summary>解析 pwsh 绝对路径；未命中抛 <see cref="PowerShellHostNotFoundException"/>。</summary>
    /// <param name="configuredPath">显式配置（ps1-runtime.json / 调用方）；空则跳过。</param>
    /// <param name="allowWindowsPowerShell51">是否允许显式回落到 Windows PowerShell 5.1（默认禁用）。</param>
    /// <param name="detailSink">完整候选清单出口（调用方写 Debug；null = 丢弃）。</param>
    public static string Locate(
        string? configuredPath = null,
        bool allowWindowsPowerShell51 = false,
        Action<string>? detailSink = null)
    {
        var resolution = ResolveDetailed(configuredPath, allowWindowsPowerShell51, detailSink);
        if (resolution.IsUsable && !string.IsNullOrWhiteSpace(resolution.ExecutablePath))
            return resolution.ExecutablePath;

        var candidates = BuildCandidates(configuredPath, allowWindowsPowerShell51);
        var distinct = candidates
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        detailSink?.Invoke("PowerShellHostLocator 未命中；候选清单：" + string.Join(" | ", distinct));
        throw new PowerShellHostNotFoundException(distinct, resolution.Failure);
    }

    /// <summary>完整解析（物化 + 可选探测）；失败返回结构化 Failure，不抛。</summary>
    public static PowerShellHostResolution ResolveDetailed(
        string? configuredPath = null,
        bool allowWindowsPowerShell51 = false,
        Action<string>? detailSink = null,
        PowerShellHostResolutionPolicy policy = PowerShellHostResolutionPolicy.TrustRealFiles)
    {
        var diagnostics = new CollectingPowerShellHostDiagnostics();
        var context = PowerShellHostContext.From(configuredPath, allowWindowsPowerShell51: allowWindowsPowerShell51);
        var resolution = PowerShellHostResolver.CreateDefault(diagnostics).Resolve(context, policy);

        if (detailSink is not null)
        {
            foreach (var line in diagnostics.Lines)
                detailSink(line);
        }

        return resolution;
    }

    /// <summary>读取 AppExecLink 真实目标（诊断/测试入口）。</summary>
    public static bool TryReadAppExecLinkTarget(string path, out string target)
        => WindowsAppExecLink.TryReadTarget(path, out target);

    /// <summary>候选序列（纯函数；顺序即优先级，供单测与诊断复算）。</summary>
    public static IReadOnlyList<string> BuildCandidates(
        string? configuredPath = null,
        bool allowWindowsPowerShell51 = false)
    {
        var context = PowerShellHostContext.From(configuredPath, allowWindowsPowerShell51: allowWindowsPowerShell51);
        return PowerShellHostResolver.CreateDefault()
            .BuildCandidates(context)
            .Select(static c => c.Path)
            .ToArray();
    }

    /// <summary>
    /// 候选可用性（结构判据唯一实现）：存在 → 解链（symlink / AppExecLink）→ 真实文件长度 &gt; 0。
    /// <para>0 字节 App Execution Alias 不会被当作可用路径；其 reparse 目标才是返回值。</para>
    /// </summary>
    public static bool TryResolveUsable(string? candidate, out string resolved)
        => PowerShellHostMaterializer.TryMaterialize(candidate, out resolved, out _);

    /// <summary>
    /// App Execution Alias 判定：仅接受 <c>%LOCALAPPDATA%\Microsoft\WindowsApps</c> 下、
    /// 且能物化为真实文件的 alias（返回真实目标，绝不返回 0 字节 alias 本身）。
    /// </summary>
    public static bool TryResolveAppExecutionAlias(string? candidate, out string resolved)
    {
        resolved = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        try
        {
            var full = Path.GetFullPath(candidate);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData)) return false;

            var aliasRoot = Path.TrimEndingDirectorySeparator(
                Path.Combine(localAppData, "Microsoft", "WindowsApps"));
            if (!full.StartsWith(aliasRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;

            return PowerShellHostMaterializer.TryMaterialize(full, out resolved, out _);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
    }
}

/// <summary>pwsh 定位失败（稳定码 + 短消息 + 全量候选仅内存/诊断面）。</summary>
public sealed class PowerShellHostNotFoundException : InvalidOperationException
{
    public PowerShellHostNotFoundException(
        IReadOnlyList<string> candidates,
        PowerShellHostFailure? failure = null)
        : base(BuildMessage(candidates, failure))
    {
        Candidates = candidates ?? Array.Empty<string>();
        Failure = failure;
    }

    public string Code => Failure?.Code ?? PowerShellHostLocator.ExecutableNotFoundCode;

    public IReadOnlyList<string> Candidates { get; }

    public PowerShellHostFailure? Failure { get; }

    private static string BuildMessage(IReadOnlyList<string> candidates, PowerShellHostFailure? failure)
    {
        if (failure is not null && !string.IsNullOrWhiteSpace(failure.ShortReason))
        {
            return failure.ShortReason.Length <= PowerShellHostLocator.ShortMessageLimit
                ? failure.ShortReason
                : failure.ShortReason[..PowerShellHostLocator.ShortMessageLimit] + "…";
        }

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
