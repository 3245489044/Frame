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
        if (opt.MaxRounds <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxRounds 须为正（无界循环禁入）。");
        var chain = policy ?? TerminationPolicyChain.Default(opt);
        var stopwatch = Stopwatch.StartNew();
        var rounds = new List<TResult>();
        var verdict = TerminationVerdict.Continue;

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
                Round: rounds.Count,
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

                // E-07：每轮成功推进后无条件心跳（推进判据只用于 Stagnation，不用于心跳），
                // 否则 Idle 策略把「从不 Beat」误读成「已空闲」。
                scope.Beat("round");
                observer?.OnBeat(scope.ScopeId, "round");

                // 未提供 macro 判据 ⇒ 不启用 Stagnation 判定（不得把「无判据」当作「无进展」）。
                if (macroProgressed is not null && opt.StagnationLimit > 0)
                {
                    noGain = macroProgressed(nextState)
                        ? 0
                        : Math.Min(noGain + 1, opt.StagnationLimit);
                }

                var currentFingerprintValue = fingerprint?.Invoke(nextState);
                sameFingerprint = currentFingerprintValue is not null && previousFingerprint is not null
                    && string.Equals(currentFingerprintValue, previousFingerprint, StringComparison.Ordinal)
                        ? sameFingerprint + 1
                        : 0;
                previousFingerprint = currentFingerprintValue;
                currentFingerprint = currentFingerprintValue;

                return nextState;
            },
            maxRounds: opt.MaxRounds,
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
                // D-04：语义判定统一交策略链（总预算由 BudgetTerminationPolicy 判）；
                // 轮次硬上限仅作物理兜底，由引擎 maxRounds 承载（到达即 Abort/UNRESOLVED，不伪装成功）。
                if (verdict.IsTerminal) return verdict.Decision;
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
            // D-05：理由映射唯一权威在 TerminationReasonMapping（禁止就地再写一套 switch）。
            decision = snapshot.FinalDecision;
            reason = TerminationReasonMapping.FromLoopDecision(decision);
            evidence = snapshot.Failure is { } failure
                ? $"{failure.Code}: {failure.Message}"
                : null;
        }
        else
        {
            // 未判定终态：失败即终止（禁兜底为 Success——E7 假绿）。
            reason = TerminationReason.Failed;
            decision = LoopDecision.Abort;
            evidence = FailureCodes.Unresolved;
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
