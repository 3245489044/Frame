using System;
using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.Loops;

/// <summary>
/// 唯一执行体（AX-ZR NoInternalRetry）：恰一次执行，体内禁 for/while/递归/失败重跑。
/// 异常 → <see cref="StepOutcome.Fail"/>（契约错误）；取消 → <see cref="StepOutcome.Fail"/>（CANCELLED）。
/// 需要第二轮的调用方只能走 <see cref="Drivers.OnceDriver"/> + <see cref="RetryPlan"/>（新 ExecutionUnit）。
/// </summary>
public static class LoopKernel
{
    /// <summary>恰一次执行：不重试、不循环、不吞并（异常一律转失败事实）。</summary>
    public static async Task<StepOutcome> ExecuteOnceAsync<TIn>(
        TIn input,
        Func<TIn, CancellationToken, Task<StepOutcome>> body,
        CancellationToken ct = default)
    {
        PortGuard.Required(body, nameof(body));

        try
        {
            return await body(input, ct).ConfigureAwait(false)
                ?? new StepOutcome.Fail(Failure.Contract(FailureCodes.ContractUnassigned, "执行体返回空结果。"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new StepOutcome.Fail(Failure.Environment(FailureCodes.Cancelled, "外部取消。"));
        }
        catch (OperationCanceledException)
        {
            return new StepOutcome.Fail(Failure.Environment(FailureCodes.Cancelled, "执行体内部取消（未由宿主令牌触发）。"));
        }
        catch (Exception ex)
        {
            return new StepOutcome.Fail(Failure.Contract(FailureCodes.Unhandled, ex.Message));
        }
    }
}
