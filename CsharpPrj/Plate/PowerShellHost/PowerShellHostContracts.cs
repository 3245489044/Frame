using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Plate.PowerShellHost;

/// <summary>候选来源（结构事实；顺序即策略）。</summary>
public enum PowerShellHostSource
{
    Bundled,
    ExplicitConfig,
    EnvironmentVariable,
    AppExecutionAliasTarget,
    StorePackage,
    InstallLocation,
    Path
}

/// <summary>失败阶段（发现 / 物化 / 验证 / 启动 / 握手）。</summary>
public enum PowerShellHostPhase
{
    Discover,
    Materialize,
    Validate,
    StartSession,
    Handshake
}

/// <summary>能力三态（Disabled 与 Unavailable 分离：设计禁用不是故障）。</summary>
public enum PowerShellHostState
{
    Unknown,
    Available,
    Disabled,
    Unavailable
}

/// <summary>解析策略：真实文件免探测 / 逐候选探测 / 只允许显式来源。</summary>
public enum PowerShellHostResolutionPolicy
{
    TrustRealFiles,
    VerifyFirst,
    ExplicitOnly
}

/// <summary>候选（Path 可为 0 字节 alias；物化后必须变成真实文件）。</summary>
public sealed record PowerShellHostCandidate(
    string Path,
    PowerShellHostSource Source,
    string? Detail = null);

/// <summary>
/// 解析上下文（显式依赖；测试可完全构造，不读进程隐式状态）。
/// </summary>
public sealed record PowerShellHostContext(
    string? ConfiguredPath = null,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    bool AllowWindowsPowerShell51 = false,
    IReadOnlyList<string>? BundledCandidates = null)
{
    public string ResolveEnvironment(string name)
    {
        if (Environment is not null
            && Environment.TryGetValue(name, out var value)
            && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return System.Environment.GetEnvironmentVariable(name) ?? string.Empty;
    }

    public string EffectiveWorkingDirectory
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(WorkingDirectory) && Directory.Exists(WorkingDirectory))
                return WorkingDirectory!;

            var current = System.Environment.CurrentDirectory;
            return !string.IsNullOrWhiteSpace(current) && Directory.Exists(current)
                ? current
                : AppContext.BaseDirectory;
        }
    }

    public static PowerShellHostContext From(
        string? configuredPath,
        string? workingDirectory = null,
        bool allowWindowsPowerShell51 = false)
        => new(configuredPath, workingDirectory, null, allowWindowsPowerShell51);
}

/// <summary>结构化失败（稳定码 + 阶段 + 原生错误码 + 详情；事件载荷只取短字段）。</summary>
public sealed record PowerShellHostFailure(
    string Code,
    PowerShellHostPhase Phase,
    string? Path,
    PowerShellHostSource? Source,
    int? NativeErrorCode,
    string ShortReason,
    string Detail)
{
    public PowerShellHostException ToException()
        => new(Code, ShortReason, Path, nativeErrorCode: NativeErrorCode);
}

/// <summary>解析结果（ExecutablePath 必须是已物化的真实文件）。</summary>
public sealed record PowerShellHostResolution(
    PowerShellHostCandidate Candidate,
    string ExecutablePath,
    bool TrustedRealFile,
    PowerShellProbeResult? Probe,
    PowerShellHostFailure? Failure)
{
    public bool IsUsable => Failure is null && (TrustedRealFile || Probe is { ExitCode: 0 });
}

/// <summary>能力快照（框架层唯一真相；Agent 只做适配）。</summary>
public sealed record PowerShellHostCapability(
    PowerShellHostState State,
    string? ReasonCode,
    string? ReasonShort,
    PowerShellHostFailure? Failure,
    string? ExecutablePath,
    PowerShellHostSource? Source,
    string? Remediation,
    DateTimeOffset ObservedAt)
{
    public static readonly PowerShellHostCapability Unknown =
        new(PowerShellHostState.Unknown, null, null, null, null, null, null, DateTimeOffset.MinValue);

    public bool IsAvailable => State == PowerShellHostState.Available;

    public static PowerShellHostCapability FromResolution(PowerShellHostResolution resolution)
        => new(
            PowerShellHostState.Available,
            null,
            null,
            null,
            resolution.ExecutablePath,
            resolution.Candidate.Source,
            null,
            DateTimeOffset.UtcNow);

    public static PowerShellHostCapability FromFailure(PowerShellHostFailure failure)
        => new(
            PowerShellHostState.Unavailable,
            failure.Code,
            failure.ShortReason,
            failure,
            failure.Path,
            failure.Source,
            RemediationFor(failure.Code),
            DateTimeOffset.UtcNow);

    private static string? RemediationFor(string code) => code switch
    {
        PowerShellHostCodes.ExecutableNotFoundCode or PowerShellHostCodes.ExecutableMissing
            => "安装 PowerShell 7，或在 ps1-runtime.json.executable / PSRUNTIME_PWSH 中给出真实 pwsh.exe 路径",
        PowerShellHostCodes.ExecutableAliasUnresolved or PowerShellHostCodes.ExecutableAliasTargetMissing
            => "App Execution Alias 无法解析；请设置 PSRUNTIME_PWSH 指向真实 pwsh.exe（不要指向 0 字节别名）",
        PowerShellHostCodes.ProbeFailed or PowerShellHostCodes.ProbeTimeout
            => "检查进程启动权限、工作目录与执行策略；详见 detailRef",
        PowerShellHostCodes.SessionStartFailed
            => "检查宿主脚本路径、-File 参数与工作目录；详见 detailRef",
        _ => null
    };
}

/// <summary>候选来源（可插拔；顺序即优先级）。</summary>
public interface IPowerShellHostCandidateSource
{
    string Name { get; }

    IEnumerable<PowerShellHostCandidate> Enumerate(PowerShellHostContext context);
}

/// <summary>候选验证器（唯一“可启动”判据）。</summary>
public interface IPowerShellHostValidator
{
    Task<PowerShellProbeResult> ProbeAsync(
        PowerShellHostCandidate candidate,
        PowerShellHostContext context,
        CancellationToken ct = default);
}

/// <summary>诊断出口（每个被拒候选都要留下结构事实）。</summary>
public interface IPowerShellHostDiagnostics
{
    void CandidateRejected(PowerShellHostCandidate candidate, string reason, Exception? error = null);

    void ResolutionFailed(PowerShellHostFailure failure);

    string WriteDetail(string code, string detail);
}

/// <summary>默认诊断丢弃器（仅用于不需要诊断的纯定位调用）。</summary>
public sealed class NullPowerShellHostDiagnostics : IPowerShellHostDiagnostics
{
    public static readonly NullPowerShellHostDiagnostics Instance = new();

    public void CandidateRejected(PowerShellHostCandidate candidate, string reason, Exception? error = null) { }

    public void ResolutionFailed(PowerShellHostFailure failure) { }

    public string WriteDetail(string code, string detail) => "inline";
}

/// <summary>内存诊断收集器（调用方一次性写日志；全套候选只在诊断面出现一次）。</summary>
public sealed class CollectingPowerShellHostDiagnostics : IPowerShellHostDiagnostics
{
    private readonly List<string> _lines = new();
    private readonly List<PowerShellHostFailure> _failures = new();

    public IReadOnlyList<string> Lines => _lines;

    public IReadOnlyList<PowerShellHostFailure> Failures => _failures;

    public void CandidateRejected(PowerShellHostCandidate candidate, string reason, Exception? error = null)
    {
        var detail = error is null ? reason : reason + " :: " + error.GetType().Name + ": " + error.Message;
        _lines.Add($"rejected source={candidate.Source} path={candidate.Path} reason={detail}");
    }

    public void ResolutionFailed(PowerShellHostFailure failure)
    {
        _failures.Add(failure);
        _lines.Add($"failed code={failure.Code} phase={failure.Phase} path={failure.Path} reason={failure.ShortReason}");
    }

    public string WriteDetail(string code, string detail)
    {
        var reference = "pshost:" + Guid.NewGuid().ToString("N")[..8];
        _lines.Add($"{reference} code={code} detail={detail}");
        return reference;
    }
}
