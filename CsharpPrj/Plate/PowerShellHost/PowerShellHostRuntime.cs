using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Plate.PowerShellHost;

/// <summary>
/// PowerShell 宿主运行时门面（框架层唯一能力生产者）：
/// 解析一次 → 发布能力快照 → 会话/脚本复用同一真实路径。
/// <para>
/// 消费方（Agent/FileDriven）只读 <see cref="Capability"/>，不再各自 Locate/Probe/分类；
/// 失败码、阶段、真实路径与 remediation 均来自框架。
/// </para>
/// </summary>
public sealed class PowerShellHostRuntime
{
    private readonly PowerShellHostResolver _resolver;
    private readonly IPowerShellHostDiagnostics _diagnostics;
    private readonly object _gate = new();
    private PowerShellHostCapability _capability = PowerShellHostCapability.Unknown;

    public PowerShellHostRuntime(
        PowerShellHostResolver? resolver = null,
        IPowerShellHostDiagnostics? diagnostics = null)
    {
        _diagnostics = diagnostics ?? NullPowerShellHostDiagnostics.Instance;
        _resolver = resolver ?? PowerShellHostResolver.CreateDefault(_diagnostics);
    }

    /// <summary>最近一次解析结果（诊断面；可用时 ExecutablePath 必为真实文件）。</summary>
    public PowerShellHostResolution? Resolution { get; private set; }

    public PowerShellHostCapability Capability
    {
        get
        {
            lock (_gate) return _capability;
        }
    }

    /// <summary>确保可用（默认 VerifyFirst：候选必须实际启动成功）。</summary>
    public async Task<PowerShellHostCapability> EnsureAvailableAsync(
        PowerShellHostContext context,
        PowerShellHostResolutionPolicy policy = PowerShellHostResolutionPolicy.VerifyFirst,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_capability.IsAvailable) return _capability;
        }

        var resolution = await _resolver.ResolveAsync(context, policy, ct).ConfigureAwait(false);
        var capability = resolution.IsUsable
            ? PowerShellHostCapability.FromResolution(resolution)
            : PowerShellHostCapability.FromFailure(resolution.Failure ?? MissingFailure(context));

        lock (_gate)
        {
            Resolution = resolution;
            _capability = capability;
        }

        return capability;
    }

    /// <summary>同步入口（组合根为同步装配面；内部仍走异步解析）。</summary>
    public PowerShellHostCapability EnsureAvailable(
        PowerShellHostContext context,
        PowerShellHostResolutionPolicy policy = PowerShellHostResolutionPolicy.VerifyFirst)
        => EnsureAvailableAsync(context, policy).GetAwaiter().GetResult();

    /// <summary>失效缓存（Store 更新 / 会话启动失败后最多重试一次）。</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            Resolution = null;
            _capability = PowerShellHostCapability.Unknown;
        }
    }

    /// <summary>创建长驻会话（保证使用同一次解析的真实路径）。</summary>
    public async Task<PowerShellStdioSession> OpenSessionAsync(
        PowerShellSessionOptions options,
        PowerShellHostContext context,
        CancellationToken ct = default)
    {
        var capability = await EnsureAvailableAsync(context, PowerShellHostResolutionPolicy.VerifyFirst, ct)
            .ConfigureAwait(false);
        if (!capability.IsAvailable || Resolution is null || string.IsNullOrWhiteSpace(Resolution.ExecutablePath))
            throw (capability.Failure ?? MissingFailure(context)).ToException();

        return new PowerShellStdioSession(options with { ResolvedExecutable = Resolution.ExecutablePath });
    }

    /// <summary>执行一次性脚本（与长驻会话共用同一真实路径）。</summary>
    public async Task<PowerShellScriptResult> RunScriptAsync(
        string scriptPath,
        IEnumerable<string>? arguments,
        PowerShellScriptOptions? options,
        PowerShellHostContext context,
        CancellationToken ct = default)
    {
        var capability = await EnsureAvailableAsync(context, PowerShellHostResolutionPolicy.VerifyFirst, ct)
            .ConfigureAwait(false);
        if (!capability.IsAvailable || Resolution is null || string.IsNullOrWhiteSpace(Resolution.ExecutablePath))
            throw (capability.Failure ?? MissingFailure(context)).ToException();

        var effective = (options ?? new PowerShellScriptOptions()) with
        {
            ResolvedExecutable = Resolution.ExecutablePath,
            WorkingDirectory = options?.WorkingDirectory ?? context.EffectiveWorkingDirectory
        };
        return await PowerShellScriptRunner.RunFileAsync(scriptPath, arguments, effective, ct)
            .ConfigureAwait(false);
    }

    private static PowerShellHostFailure MissingFailure(PowerShellHostContext context)
        => new(
            PowerShellHostLocator.ExecutableNotFoundCode,
            PowerShellHostPhase.Discover,
            null,
            null,
            null,
            "未找到可用的 pwsh",
            "cwd=" + context.EffectiveWorkingDirectory);
}
