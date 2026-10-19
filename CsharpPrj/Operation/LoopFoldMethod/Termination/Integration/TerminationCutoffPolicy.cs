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
    private readonly TerminationOptions _options;

    private string? _previousFingerprint;
    private int _sameFingerprintRounds;

    public TerminationCutoffPolicy(
        TerminationScope scope,
        ITerminationPolicy? policy = null,
        ICutoffPolicy<TState>? inner = null,
        Func<TState, string>? fingerprint = null,
        TerminationOptions? options = null)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _options = options ?? TerminationOptions.Default;
        _policy = policy ?? TerminationPolicyChain.Default(_options);
        _inner = inner;
        _fingerprint = fingerprint;
    }

    public FoldCut Decide(TState head, int coordinate, FoldBudget budget)
    {
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
            IsFatal: false,
            Fingerprint: fingerprint,
            SameFingerprintRounds: _sameFingerprintRounds,
            NoGain: 0,
            NoGainLimit: _options.StagnationLimit);

        var verdict = _policy.Decide(context);
        if (verdict.IsTerminal) return TerminationReasonMapping.ToFoldCut(verdict.Reason);

        if (_inner is not null) return _inner.Decide(head, coordinate, budget);

        return budget.Exhausted(coordinate) ? FoldCut.BudgetExhausted : FoldCut.Continue;
    }
}
