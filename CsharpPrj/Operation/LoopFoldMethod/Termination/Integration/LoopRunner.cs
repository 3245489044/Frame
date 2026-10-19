using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Modules.Threading;
using Share.TaskStatusPool.Loops;

namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 循环运行器 —— 通用有界循环的唯一入口：把 <see cref="TerminationScope"/> + <see cref="ITerminationPolicy"/>
/// 接到既有 <see cref="SnapshotDrivenLoopEngine{TState,TResult}"/>（TH-12 单一 while，不另写循环）。
/// <para>终止二分（AX.TerminationBipartition）：预算/空闲/失败/空转为引擎内裁决（策略链），
/// 取消/宿主关闭为探索终止（不落预算术语义）。任何循环必被有界：轮次硬上限兜底。</para>
/// </summary>
public sealed class LoopRunner<TState, TResult>
{
    public async Task<LoopRunResult<TState, TResult>> RunAsync(
        TState initialState,
        Func<TState, CancellationToken, Task<TResult>> execute,
        Func<TState, TResult, CancellationToken, Task<TState>> next,
        TerminationScope scope,
        ITerminationPolicy? policy = null,
        Func<TState, string>? fingerprint = null,
        Func<TState, bool>? macroProgressed = null,
        Func<TState, bool>? isFatal = null,
        TerminationOptions? options = null,
        ITerminationProbe? probe = null,
        ITerminationObserver? observer = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(scope);

        var opt = options ?? TerminationOptions.Default;
        var chain = policy ?? TerminationPolicyChain.Default(opt);
        var stopwatch = Stopwatch.StartNew();
        var rounds = new List<TResult>();
        var verdict = TerminationVerdict.Continue;

        var round = 0;
        var noGain = 0;
        var sameFingerprint = 0;
        string? previousFingerprint = null;
        string? currentFingerprint = null;

        using var linked = scope.Token.CanBeCanceled && ct.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(scope.Token, ct)
            : null;
        var token = linked?.Token ?? (scope.Token.CanBeCanceled ? scope.Token : ct);

        TerminationContext Build(TState state)
            => new(
                ScopeId: scope.ScopeId,
                Owner: scope.Owner,
                Round: round,
                Elapsed: scope.Elapsed,
                Idle: scope.IdleFor,
                IdleWindow: scope.IdleWindow ?? TimeSpan.Zero,
                Remaining: scope.Remaining,
                HasDeadline: scope.HasDeadline,
                IsFatal: isFatal?.Invoke(state) ?? false,
                Fingerprint: currentFingerprint,
                SameFingerprintRounds: sameFingerprint,
                NoGain: noGain,
                NoGainLimit: opt.StagnationLimit,
                HostClosed: probe?.IsHostClosed ?? false);

        var engine = new SnapshotDrivenLoopEngine<TState, TResult>();
        var snapshot = await engine.RunAsync(
            initialState: initialState,
            execute: async (state, t) =>
            {
                var result = await execute(state, t).ConfigureAwait(false);
                rounds.Add(result);
                return result;
            },
            next: async (state, result, t) =>
            {
                var nextState = await next(state, result, t).ConfigureAwait(false);

                var progressed = macroProgressed?.Invoke(nextState) ?? false;
                if (progressed)
                {
                    noGain = 0;
                    scope.Beat("round");
                    observer?.OnBeat(scope.ScopeId, "round");
                }
                else if (opt.StagnationLimit > 0)
                {
                    noGain = Math.Min(noGain + 1, opt.StagnationLimit);
                }

                var currentFingerprintValue = fingerprint?.Invoke(nextState);
                sameFingerprint = currentFingerprintValue is not null && previousFingerprint is not null
                    && string.Equals(currentFingerprintValue, previousFingerprint, StringComparison.Ordinal)
                        ? sameFingerprint + 1
                        : 0;
                previousFingerprint = currentFingerprintValue;
                currentFingerprint = currentFingerprintValue;

                round++;
                return nextState;
            },
            ct: token,
            externalTerminate: (state, _) =>
            {
                var decided = chain.Decide(Build(state));
                if (!decided.IsTerminal) return Task.FromResult(false);
                verdict = decided;
                observer?.OnVerdict(decided);
                return Task.FromResult(true);
            },
            budgetTerminate: state =>
            {
                if (verdict.IsTerminal) return verdict.Decision;

                if (opt.MaxRounds > 0 && round >= opt.MaxRounds)
                {
                    verdict = TerminationVerdict.Terminal(
                        LoopDecision.Timeout, TerminationReason.BudgetExhausted,
                        $"轮次达到硬上限 {opt.MaxRounds}");
                    observer?.OnVerdict(verdict);
                    return verdict.Decision;
                }

                if (scope.HasDeadline && scope.Remaining <= TimeSpan.Zero)
                {
                    verdict = TerminationVerdict.Terminal(
                        LoopDecision.Timeout, TerminationReason.BudgetExhausted,
                        "总预算耗尽（引擎内裁决）");
                    observer?.OnVerdict(verdict);
                    return verdict.Decision;
                }

                return LoopDecision.Continue;
            }).ConfigureAwait(false);

        stopwatch.Stop();
        return Conclude(snapshot, verdict, scope, observer, stopwatch.Elapsed);
    }

    private static LoopRunResult<TState, TResult> Conclude(
        SnapshotLoopResult<TState, TResult> snapshot,
        TerminationVerdict verdict,
        TerminationScope scope,
        ITerminationObserver? observer,
        TimeSpan elapsed)
    {
        TerminationReason reason;
        LoopDecision decision;
        string? evidence;

        if (verdict.IsTerminal)
        {
            reason = verdict.Reason;
            decision = verdict.Decision;
            evidence = verdict.Evidence;
        }
        else if (snapshot.TerminatedByExternal)
        {
            reason = TerminationReason.Cancelled;
            decision = LoopDecision.Continue;
            evidence = "外部取消（探索终止）";
        }
        else if (snapshot.TerminatedByBudget)
        {
            decision = snapshot.FinalDecision;
            reason = decision switch
            {
                LoopDecision.Success => TerminationReason.Completed,
                LoopDecision.Timeout => TerminationReason.BudgetExhausted,
                LoopDecision.TripTerminated => TerminationReason.Failed,
                _ => TerminationReason.None,
            };
            evidence = null;
        }
        else
        {
            reason = TerminationReason.Completed;
            decision = LoopDecision.Success;
            evidence = null;
        }

        // 强制退出语义：宿主关闭/失败/超时/空闲/空转 → 立即触发 scope 中止回调（不等被取消方返回）。
        if (reason == TerminationReason.Completed)
        {
            scope.Complete();
        }
        else if (reason is not (TerminationReason.None or TerminationReason.Cancelled))
        {
            if (scope.Terminate(reason, evidence) && scope.Request is { } request)
                observer?.OnTerminated(request);
        }

        return new LoopRunResult<TState, TResult>(
            snapshot.FinalState,
            snapshot.Rounds.Select(static round => round.Result).ToArray(),
            decision,
            reason,
            TerminationReasonMapping.ToOutcome(reason, evidence),
            evidence,
            elapsed);
    }
}
