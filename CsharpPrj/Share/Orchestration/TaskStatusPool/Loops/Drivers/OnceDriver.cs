using System;
using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.Loops.Drivers;

/// <summary>
/// 一次执行壳（唯一允许「第二执行单元」的位置）：恰一次执行；仅当调用方显式提供
/// <see cref="RetryPlan"/> 且账本允许时，才以「新 ExecutionUnit」再执行一次。
/// 壳内无 for/while：第二单元是一次尾调用，不是循环。
/// </summary>
public sealed class OnceDriver
{
    private readonly AttemptLedger _ledger;

    public OnceDriver(AttemptLedger? ledger = null) => _ledger = ledger ?? new AttemptLedger();

    public AttemptLedger Ledger => _ledger;

    /// <summary>恰一次执行（无重试）。</summary>
    public Task<StepOutcome> RunAsync<TIn>(
        TIn input,
        Func<TIn, CancellationToken, Task<StepOutcome>> body,
        CancellationToken ct = default)
        => LoopKernel.ExecuteOnceAsync(input, body, ct);

    /// <summary>
    /// 执行一次；失败且 <paramref name="plan"/> 合法时，冻结账本并执行第二个 ExecutionUnit。
    /// 非法/无理由的重试 → <c>Fail(INVALID_RETRY)</c>，不重跑。
    /// </summary>
    public async Task<StepOutcome> RunAsync<TIn>(
        TIn input,
        Func<TIn, CancellationToken, Task<StepOutcome>> body,
        RetryPlan? plan,
        CancellationToken ct = default)
    {
        var outcome = await LoopKernel.ExecuteOnceAsync(input, body, ct).ConfigureAwait(false);
        if (outcome is StepOutcome.Ok || plan is null) return outcome;

        if (!RetryPolicy.IsValid(plan, _ledger))
            return new StepOutcome.Fail(Failure.Contract(
                FailureCodes.InvalidRetry,
                "重试提议非法（无理由 / 同输入 / 配额未授予）。"));

        _ledger.Advance(plan, plan.ChangeDigest);
        return await LoopKernel.ExecuteOnceAsync(input, body, ct).ConfigureAwait(false);
    }
}
