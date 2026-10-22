using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Plate.PowerShellHost;

/// <summary>
/// 可执行物化（结构判据唯一实现）：存在 → 解链（symlink / AppExecLink）→ 真实文件且长度 &gt; 0。
/// <para>0 字节 App Execution Alias 永远不会作为 ExecutablePath 返回。</para>
/// </summary>
public static class PowerShellHostMaterializer
{
    public static bool TryMaterialize(string? candidate, out string resolved, out string reason)
    {
        resolved = string.Empty;
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            reason = "empty-path";
            return false;
        }

        try
        {
            if (!File.Exists(candidate))
            {
                reason = "not-found";
                return false;
            }

            var full = Path.GetFullPath(candidate);
            var info = new FileInfo(full);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                var target = File.ResolveLinkTarget(full, returnFinalTarget: true);
                if (target is not null && target.Exists)
                {
                    var targetInfo = new FileInfo(target.FullName);
                    if (targetInfo.Length > 0)
                    {
                        resolved = targetInfo.FullName;
                        reason = "symlink-target";
                        return true;
                    }
                }

                if (WindowsAppExecLink.TryReadTarget(full, out var appTarget) && File.Exists(appTarget))
                {
                    var targetInfo = new FileInfo(appTarget);
                    if (targetInfo.Length > 0)
                    {
                        resolved = targetInfo.FullName;
                        reason = "appexec-target";
                        return true;
                    }

                    reason = "appexec-target-zero-length";
                    return false;
                }

                reason = "reparse-unresolved";
                return false;
            }

            if (info.Length <= 0)
            {
                reason = "zero-length";
                return false;
            }

            resolved = full;
            reason = "real-file";
            return true;
        }
        catch (IOException ex) { reason = "io:" + ex.Message; return false; }
        catch (UnauthorizedAccessException ex) { reason = "access:" + ex.Message; return false; }
        catch (ArgumentException ex) { reason = "argument:" + ex.Message; return false; }
        catch (NotSupportedException ex) { reason = "unsupported:" + ex.Message; return false; }
    }
}

/// <summary>显式配置来源（最高优先；部署锁定用它）。</summary>
public sealed class ExplicitPowerShellHostSource : IPowerShellHostCandidateSource
{
    public string Name => "explicit-config";

    public IEnumerable<PowerShellHostCandidate> Enumerate(PowerShellHostContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.ConfiguredPath))
            yield return new PowerShellHostCandidate(context.ConfiguredPath!, PowerShellHostSource.ExplicitConfig);
    }
}

/// <summary>环境变量来源（用户级覆盖，不改仓库配置）。</summary>
public sealed class EnvironmentPowerShellHostSource : IPowerShellHostCandidateSource
{
    public string Name => "environment-variable";

    public IEnumerable<PowerShellHostCandidate> Enumerate(PowerShellHostContext context)
    {
        var path = context.ResolveEnvironment("PSRUNTIME_PWSH");
        if (!string.IsNullOrWhiteSpace(path))
            yield return new PowerShellHostCandidate(path, PowerShellHostSource.EnvironmentVariable);
    }
}

/// <summary>随产品分发的 pwsh（最确定性；未配置则不产生候选）。</summary>
public sealed class BundledPowerShellHostSource : IPowerShellHostCandidateSource
{
    public string Name => "bundled";

    public IEnumerable<PowerShellHostCandidate> Enumerate(PowerShellHostContext context)
    {
        if (context.BundledCandidates is not null)
        {
            foreach (var path in context.BundledCandidates)
                if (!string.IsNullOrWhiteSpace(path))
                    yield return new PowerShellHostCandidate(path, PowerShellHostSource.Bundled);
            yield break;
        }

        var configured = context.ResolveEnvironment("PSRUNTIME_PWSH_BUNDLED");
        if (!string.IsNullOrWhiteSpace(configured))
            yield return new PowerShellHostCandidate(configured, PowerShellHostSource.Bundled);
    }
}

/// <summary>
/// Store App Execution Alias 来源：只产出 alias 路径，全部交给物化器解析真实目标。
/// 目录枚举（%LOCALAPPDATA%\Microsoft\WindowsApps）通常可访问；失败可见化。
/// </summary>
public sealed class AppExecutionAliasPowerShellHostSource : IPowerShellHostCandidateSource
{
    private readonly IPowerShellHostDiagnostics _diagnostics;

    public AppExecutionAliasPowerShellHostSource(IPowerShellHostDiagnostics? diagnostics = null)
        => _diagnostics = diagnostics ?? NullPowerShellHostDiagnostics.Instance;

    public string Name => "appexec-alias";

    public IEnumerable<PowerShellHostCandidate> Enumerate(PowerShellHostContext context)
    {
        var localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData)) yield break;

        var root = Path.Combine(localAppData, "Microsoft", "WindowsApps");
        if (!Directory.Exists(root)) yield break;

        var plain = Path.Combine(root, "pwsh.exe");
        if (File.Exists(plain))
            yield return new PowerShellHostCandidate(plain, PowerShellHostSource.AppExecutionAliasTarget, "alias-root");

        foreach (var directory in EnumerateDirectories(root, "Microsoft.PowerShell_*"))
        {
            var candidate = Path.Combine(directory, "pwsh.exe");
            if (File.Exists(candidate))
                yield return new PowerShellHostCandidate(candidate, PowerShellHostSource.AppExecutionAliasTarget, "alias-package-family");
        }
    }

    private IEnumerable<string> EnumerateDirectories(string root, string pattern)
    {
        string[] directories;
        try
        {
            directories = Directory.GetDirectories(root, pattern, SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _diagnostics.CandidateRejected(
                new PowerShellHostCandidate(root, PowerShellHostSource.AppExecutionAliasTarget),
                "directory-enumeration-failed",
                ex);
            yield break;
        }

        foreach (var directory in directories.OrderBy(static d => d, StringComparer.OrdinalIgnoreCase))
            yield return directory;
    }
}

/// <summary>
/// Store 包目录来源：仅当目录可访问时枚举；ACL 拒绝必须可见（本次故障的关键盲区）。
/// </summary>
public sealed class StorePackagePowerShellHostSource : IPowerShellHostCandidateSource
{
    private static readonly System.Text.RegularExpressions.Regex PackageVersionPattern = new(
        @"^Microsoft\.PowerShell_(\d+(?:\.\d+){1,3})_",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly IPowerShellHostDiagnostics _diagnostics;

    public StorePackagePowerShellHostSource(IPowerShellHostDiagnostics? diagnostics = null)
        => _diagnostics = diagnostics ?? NullPowerShellHostDiagnostics.Instance;

    public string Name => "store-package";

    public IEnumerable<PowerShellHostCandidate> Enumerate(PowerShellHostContext context)
    {
        var programFiles = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles);
        var programW6432 = context.ResolveEnvironment("ProgramW6432");
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(programFiles))
            roots.Add(Path.Combine(programFiles, "WindowsApps"));
        if (!string.IsNullOrWhiteSpace(programW6432))
            roots.Add(Path.Combine(programW6432, "WindowsApps"));

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;

            string[] packages;
            try
            {
                packages = Directory.GetDirectories(root, "Microsoft.PowerShell_*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // 非打包进程在 C:\Program Files\WindowsApps 常被 ACL 拒绝：显式记录，不再静默。
                _diagnostics.CandidateRejected(
                    new PowerShellHostCandidate(root, PowerShellHostSource.StorePackage),
                    "store-root-inaccessible",
                    ex);
                continue;
            }

            foreach (var package in packages
                         .OrderByDescending(PackageVersion)
                         .ThenBy(static p => p, StringComparer.OrdinalIgnoreCase)
                         .Take(PowerShellHostLocator.MaxCandidatesPerRoot))
            {
                var candidate = Path.Combine(package, "pwsh.exe");
                if (File.Exists(candidate))
                    yield return new PowerShellHostCandidate(candidate, PowerShellHostSource.StorePackage, Path.GetFileName(package));
            }
        }
    }

    private static Version PackageVersion(string directory)
    {
        var match = PackageVersionPattern.Match(Path.GetFileName(directory));
        return match.Success && Version.TryParse(match.Groups[1].Value, out var version)
            ? version
            : new Version(0, 0);
    }
}

/// <summary>常规安装位来源（MSI / Program Files）。</summary>
public sealed class InstallLocationPowerShellHostSource : IPowerShellHostCandidateSource
{
    public string Name => "install-location";

    public IEnumerable<PowerShellHostCandidate> Enumerate(PowerShellHostContext context)
    {
        foreach (var root in new[]
                 {
                     System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),
                     System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86),
                     context.ResolveEnvironment("ProgramW6432")
                 })
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var candidate = Path.Combine(root, "PowerShell", "7", "pwsh.exe");
            if (File.Exists(candidate))
                yield return new PowerShellHostCandidate(candidate, PowerShellHostSource.InstallLocation);
        }
    }
}

/// <summary>PATH 来源（只接受真实文件；alias 交给物化器解析）。</summary>
public sealed class PathPowerShellHostSource : IPowerShellHostCandidateSource
{
    public string Name => "path";

    public IEnumerable<PowerShellHostCandidate> Enumerate(PowerShellHostContext context)
    {
        foreach (var directory in context.ResolveEnvironment("PATH")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = directory.Trim().Trim('"');
            if (dir.Length == 0) continue;

            var candidate = Path.Combine(dir, "pwsh.exe");
            if (File.Exists(candidate))
                yield return new PowerShellHostCandidate(candidate, PowerShellHostSource.Path);
        }

        if (!context.AllowWindowsPowerShell51) yield break;

        foreach (var directory in context.ResolveEnvironment("PATH")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = directory.Trim().Trim('"');
            if (dir.Length == 0) continue;

            var candidate = Path.Combine(dir, "powershell.exe");
            if (File.Exists(candidate))
                yield return new PowerShellHostCandidate(candidate, PowerShellHostSource.Path, "windows-powershell-5.1");
        }

        var system32 = System.Environment.GetFolderPath(System.Environment.SpecialFolder.System);
        if (!string.IsNullOrWhiteSpace(system32))
        {
            var fallback = Path.Combine(system32, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(fallback))
                yield return new PowerShellHostCandidate(fallback, PowerShellHostSource.Path, "windows-powershell-5.1");
        }
    }
}

/// <summary>
/// 解析器：Discovery（有序来源）→ Materialization（真实文件）→ Validation（可选探测）。
/// <para>策略与机制分离：候选顺序是数据；Windows 语义在各 Source / AppExecLink 内。</para>
/// </summary>
public sealed class PowerShellHostResolver
{
    private readonly IReadOnlyList<IPowerShellHostCandidateSource> _sources;
    private readonly IPowerShellHostValidator _validator;
    private readonly IPowerShellHostDiagnostics _diagnostics;

    public PowerShellHostResolver(
        IEnumerable<IPowerShellHostCandidateSource> sources,
        IPowerShellHostValidator validator,
        IPowerShellHostDiagnostics? diagnostics = null)
    {
        _sources = (sources ?? throw new ArgumentNullException(nameof(sources))).ToArray();
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _diagnostics = diagnostics ?? NullPowerShellHostDiagnostics.Instance;
    }

    public static PowerShellHostResolver CreateDefault(IPowerShellHostDiagnostics? diagnostics = null)
    {
        var sink = diagnostics ?? NullPowerShellHostDiagnostics.Instance;
        return new PowerShellHostResolver(
            new IPowerShellHostCandidateSource[]
            {
                new BundledPowerShellHostSource(),
                new ExplicitPowerShellHostSource(),
                new EnvironmentPowerShellHostSource(),
                new AppExecutionAliasPowerShellHostSource(sink),
                new StorePackagePowerShellHostSource(sink),
                new InstallLocationPowerShellHostSource(),
                new PathPowerShellHostSource()
            },
            new PowerShellHostProbeValidator(),
            sink);
    }

    /// <summary>候选序列（纯枚举；供诊断/兼容 API 复算）。</summary>
    public IReadOnlyList<PowerShellHostCandidate> BuildCandidates(
        PowerShellHostContext context,
        PowerShellHostResolutionPolicy policy = PowerShellHostResolutionPolicy.TrustRealFiles)
    {
        var result = new List<PowerShellHostCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in _sources)
        {
            if (policy == PowerShellHostResolutionPolicy.ExplicitOnly
                && source is not (ExplicitPowerShellHostSource or EnvironmentPowerShellHostSource))
            {
                continue;
            }

            try
            {
                foreach (var candidate in source.Enumerate(context))
                {
                    if (string.IsNullOrWhiteSpace(candidate.Path)) continue;
                    if (seen.Add(candidate.Path)) result.Add(candidate);
                }
            }
            catch (Exception ex)
            {
                _diagnostics.CandidateRejected(
                    new PowerShellHostCandidate(string.Empty, PowerShellHostSource.Path, source.Name),
                    "source-failed",
                    ex);
            }
        }

        return result;
    }

    public PowerShellHostResolution Resolve(
        PowerShellHostContext context,
        PowerShellHostResolutionPolicy policy = PowerShellHostResolutionPolicy.TrustRealFiles)
        => ResolveAsync(context, policy).GetAwaiter().GetResult();

    public async Task<PowerShellHostResolution> ResolveAsync(
        PowerShellHostContext context,
        PowerShellHostResolutionPolicy policy = PowerShellHostResolutionPolicy.TrustRealFiles,
        CancellationToken ct = default)
    {
        var candidates = BuildCandidates(context, policy);
        PowerShellHostFailure? lastFailure = null;
        var resolvedSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (!PowerShellHostMaterializer.TryMaterialize(candidate.Path, out var realPath, out var reason))
            {
                _diagnostics.CandidateRejected(candidate, reason);
                lastFailure = BuildMaterializationFailure(candidate, reason);
                continue;
            }

            if (!resolvedSeen.Add(realPath)) continue;

            var materialized = candidate with
            {
                Path = realPath,
                Detail = candidate.Detail ?? candidate.Path
            };

            if (policy != PowerShellHostResolutionPolicy.VerifyFirst)
                return new PowerShellHostResolution(materialized, realPath, TrustedRealFile: true, Probe: null, Failure: null);

            try
            {
                var probe = await _validator.ProbeAsync(materialized, context, ct).ConfigureAwait(false);
                if (probe.ExitCode == 0)
                    return new PowerShellHostResolution(materialized, realPath, TrustedRealFile: false, probe, Failure: null);

                lastFailure = new PowerShellHostFailure(
                    PowerShellHostCodes.ProbeFailed,
                    PowerShellHostPhase.Validate,
                    realPath,
                    candidate.Source,
                    null,
                    $"PowerShell 探测失败（exit={probe.ExitCode}）：{realPath}",
                    probe.StandardError);
                _diagnostics.CandidateRejected(candidate, "probe-exit-" + probe.ExitCode);
            }
            catch (PowerShellHostException ex)
            {
                lastFailure = new PowerShellHostFailure(
                    ex.Code,
                    PowerShellHostPhase.Validate,
                    realPath,
                    candidate.Source,
                    ex.NativeErrorCode,
                    ex.Message,
                    ex.Detail ?? ex.ToString());
                _diagnostics.CandidateRejected(candidate, "probe-failed:" + ex.Code, ex);

                // 物化目标不可启动（如 WindowsApps 实文件在当前宿主被拒绝）时，回退到 alias 启动路径。
                // alias 仅作为“已验证可启动”的启动路径，不冒充真实文件；失败仍保留原稳定码。
                if (candidate.Source == PowerShellHostSource.AppExecutionAliasTarget
                    && !string.Equals(candidate.Path, realPath, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var aliasProbe = await _validator.ProbeAsync(candidate, context, ct).ConfigureAwait(false);
                        if (aliasProbe.ExitCode == 0)
                        {
                            _diagnostics.CandidateRejected(candidate, "materialized-target-denied; alias-launch-ok");
                            return new PowerShellHostResolution(
                                candidate with { Detail = "alias-launch" },
                                candidate.Path,
                                TrustedRealFile: false,
                                aliasProbe,
                                Failure: null);
                        }
                    }
                    catch (PowerShellHostException aliasEx)
                    {
                        _diagnostics.CandidateRejected(candidate, "alias-launch-failed:" + aliasEx.Code, aliasEx);
                        lastFailure = new PowerShellHostFailure(
                            aliasEx.Code,
                            PowerShellHostPhase.Validate,
                            candidate.Path,
                            candidate.Source,
                            aliasEx.NativeErrorCode,
                            aliasEx.Message,
                            aliasEx.Detail ?? aliasEx.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                lastFailure = new PowerShellHostFailure(
                    PowerShellHostCodes.ProbeFailed,
                    PowerShellHostPhase.Validate,
                    realPath,
                    candidate.Source,
                    (ex as System.ComponentModel.Win32Exception)?.NativeErrorCode,
                    "PowerShell 探测失败：" + ex.Message,
                    ex.ToString());
                _diagnostics.CandidateRejected(candidate, "probe-failed", ex);
            }
        }

        var failure = lastFailure ?? new PowerShellHostFailure(
            PowerShellHostLocator.ExecutableNotFoundCode,
            PowerShellHostPhase.Discover,
            null,
            null,
            null,
            "未找到可用的 pwsh",
            "候选数=" + candidates.Count);

        _diagnostics.ResolutionFailed(failure);
        return new PowerShellHostResolution(
            new PowerShellHostCandidate(string.Empty, failure.Source ?? PowerShellHostSource.Path),
            string.Empty,
            TrustedRealFile: false,
            Probe: null,
            failure);
    }

    private static PowerShellHostFailure BuildMaterializationFailure(PowerShellHostCandidate candidate, string reason)
    {
        var aliasLike = candidate.Source == PowerShellHostSource.AppExecutionAliasTarget
                        || candidate.Path.Contains("\\Microsoft\\WindowsApps\\", StringComparison.OrdinalIgnoreCase);
        var code = aliasLike
            ? (reason.Contains("target-zero-length", StringComparison.OrdinalIgnoreCase)
                ? PowerShellHostCodes.ExecutableAliasTargetMissing
                : PowerShellHostCodes.ExecutableAliasUnresolved)
            : PowerShellHostCodes.ExecutableMissing;

        return new PowerShellHostFailure(
            code,
            PowerShellHostPhase.Materialize,
            candidate.Path,
            candidate.Source,
            null,
            $"PowerShell 宿主不可用（{code}）：{reason}",
            reason + " @ " + candidate.Path);
    }
}

/// <summary>默认验证器：把候选交给 <see cref="PowerShellHostProbe"/>（显式工作目录 + 并发双流）。</summary>
public sealed class PowerShellHostProbeValidator : IPowerShellHostValidator
{
    public Task<PowerShellProbeResult> ProbeAsync(
        PowerShellHostCandidate candidate,
        PowerShellHostContext context,
        CancellationToken ct = default)
        => PowerShellHostProbe.ProbeAsync(
            candidate.Path,
            workingDirectory: context.EffectiveWorkingDirectory,
            ct: ct);
}

