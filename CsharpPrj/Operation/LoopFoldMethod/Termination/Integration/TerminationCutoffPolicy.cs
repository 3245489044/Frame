using System;
using Modules.Threading;
using Operation.LoopFoldMethod.Cutoff;
using Operation.LoopFoldMethod.Fold;

namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 折叠截断适配器 —— 把「终止作用域 + 终止策略链」接到既有
/// <see cref="ICutoffPolicy{TState}"/>（<c>FoldEngine</c> 零改动接入）。
/// <para>优先级：策略链终值 → 内层截断策略（可为 null）→ 预算底线。内层为 null 时仍保留
/// <see cref="FoldBudget"/> 强制截断（信任反转：不信任外部算子自停）。</para>
/// </summary>
public sealed class TerminationCutoffPolicy<TState> : ICutoffPolicy<TState>
{
    private readonly TerminationScope _scope;
    private readonly ITerminationPolicy _policy;
    private readonly ICutoffPolicy<TState>? _inner;
    private readonly Func<TState, string>? _fingerprint;
    private readonly Func<TState, bool>? _isFatal;
    private readonly Func<TState, int>? _noGain;
    private readonly Func<TState, bool>? _hostClosed;
    private readonly TerminationOptions _options;

    private string? _previousFingerprint;
    private int _sameFingerprintRounds;

    // E-03：同一坐标只裁决一次（引擎每轮头/末双调用，指纹计数不得被重复累加）。
    private int _lastCoordinate = int.MinValue;
    private FoldCut _lastCut = FoldCut.Continue;

    /// <param name="isFatal">结构事实：该折叠头是否已失败（接入 FailureHaltPolicy）。缺省 = 恒 false。</param>
    /// <param name="noGain">结构事实：macro 判据连续无推进轮数（接入 StagnationTerminationPolicy）。缺省 = 同指纹轮数。</param>
    /// <param name="hostClosed">结构事实：宿主是否已关闭（接入 HostClosedPolicy）。缺省 = 恒 false。</param>
    public TerminationCutoffPolicy(
        TerminationScope scope,
        ITerminationPolicy? policy = null,
        ICutoffPolicy<TState>? inner = null,
        Func<TState, string>? fingerprint = null,
        TerminationOptions? options = null,
        Func<TState, bool>? isFatal = null,
        Func<TState, int>? noGain = null,
        Func<TState, bool>? hostClosed = null)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _options = options ?? TerminationOptions.Default;
        _policy = policy ?? TerminationPolicyChain.Default(_options);
        _inner = inner;
        _fingerprint = fingerprint;
        _isFatal = isFatal;
        _noGain = noGain;
        _hostClosed = hostClosed;
    }

    public FoldCut Decide(TState head, int coordinate, FoldBudget budget)
    {
        // 同一坐标重复调用直接返回缓存（不重复推进指纹计数）。
        if (coordinate == _lastCoordinate)
            return _lastCut;

        var fingerprint = _fingerprint?.Invoke(head);
        _sameFingerprintRounds = fingerprint is not null && _previousFingerprint is not null
            && string.Equals(fingerprint, _previousFingerprint, StringComparison.Ordinal)
                ? _sameFingerprintRounds + 1
                : 0;
        _previousFingerprint = fingerprint;

        var context = new TerminationContext(
            ScopeId: _scope.ScopeId,
            Owner: _scope.Owner,
            Round: coordinate,
            Elapsed: _scope.Elapsed,
            Idle: _scope.IdleFor,
            IdleWindow: _scope.IdleWindow ?? TimeSpan.Zero,
            Remaining: _scope.Remaining,
            HasDeadline: _scope.HasDeadline,
            // E-08：结构事实必须来自真实折叠头，否则 FailureHalt/Stagnation/HostClosed 三条策略恒不可达。
            IsFatal: _isFatal?.Invoke(head) ?? false,
            Fingerprint: fingerprint,
            SameFingerprintRounds: _sameFingerprintRounds,
            NoGain: _noGain?.Invoke(head) ?? _sameFingerprintRounds,
            NoGainLimit: _options.StagnationLimit,
            HostClosed: _hostClosed?.Invoke(head) ?? false);

        var verdict = _policy.Decide(context);
        var cut = verdict.IsTerminal
            ? TerminationReasonMapping.ToFoldCut(verdict.Reason)
            : _inner?.Decide(head, coordinate, budget) ?? budget.Decide(coordinate);

        _lastCoordinate = coordinate;
        _lastCut = cut;
        return cut;
    }
}
