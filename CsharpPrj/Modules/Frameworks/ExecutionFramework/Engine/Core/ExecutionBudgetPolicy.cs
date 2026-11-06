using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Modules.Parsing.Invocation;
using Modules.Threading;

namespace ExecutionFramework.Engine.Core;

/// <summary>执行预算结局（四态；<see cref="NotBudgeted"/> 表示未声明 deadline，直通执行）。</summary>
public enum ExecutionBudgetOutcome
{
    /// <summary>未声明命令级预算 → 直通执行，结局由命令体自定。</summary>
    NotBudgeted,

    /// <summary>预算窗口内完成（胜出）→ 结果有效。</summary>
    Completed,

    /// <summary>预算耗尽 → 命令被放弃，迟到结果不得回写（由调用方落终态）。</summary>
    TimedOut,

    /// <summary>外层取消（宿主终止/窗体关闭）优先于预算 → 按取消上抛语义处理。</summary>
    OuterCanceled,
}

/// <summary>执行预算结局 + 结果回收：<see cref="Value"/> 仅在 <see cref="ExecutionBudgetOutcome.Completed"/> 有效。</summary>
public readonly record struct ExecutionBudgetResult<T>(ExecutionBudgetOutcome Outcome, T? Value, long? BudgetMs)
{
    public bool IsCompleted => Outcome is ExecutionBudgetOutcome.Completed or ExecutionBudgetOutcome.NotBudgeted;
}

/// <summary>
/// 执行预算策略（Modules B-1 通用化）：把「从参数解析命令级 deadline + 有界执行 + 三态分类 + 结果回收隔离」
/// 从各调用点内联代码收敛为单一实现。
/// <para>
/// 竞速原语不重复实现：复用 <see cref="BoundedRun"/>（Modules.Threading 单源，外层取消优先 + 优雅排空）；
/// 本类只负责领域策略——预算解析、结局映射与「超时即放弃、迟到结果不回收」的契约。
/// </para>
/// </summary>
public static class ExecutionBudgetPolicy
{
    /// <summary>默认预算参数键（命令面 <c>timeout=&lt;ms&gt;</c>）。</summary>
    public const string DefaultBudgetKey = "timeout";

    /// <summary>
    /// 从命令参数解析正数预算毫秒；缺失/非法/≤0 → null（表示「未声明 deadline」）。
    /// <para>读取实现 = <c>InvocationArgs.TimeoutBudgetMs</c>（调用面参数读取唯一实现）；</para>
    /// <para>非法值不在此静默：加载期由 <c>InvocationBinding</c> 报 R2（ArgumentValueInvalid），运行期命令层可先行拒绝。</para>
    /// </summary>
    public static long? TryReadBudgetMs(
        IReadOnlyDictionary<string, object?>? parameters,
        string key = DefaultBudgetKey)
        => InvocationArgs.TimeoutBudgetMs(parameters, key);

    /// <summary>
    /// 有界执行：<paramref name="budgetMs"/> 为 null/≤0 → 直通（<see cref="ExecutionBudgetOutcome.NotBudgeted"/>）；
    /// 否则经 <see cref="BoundedRun"/> 竞速并映射三态。超时/外层取消时迟到结果被丢弃（不进入 <see cref="ExecutionBudgetResult{T}.Value"/>）。
    /// </summary>
    /// <exception cref="OperationCanceledException">未声明预算时命令体自身抛出的取消异常按原语义外泄。</exception>
    public static async Task<ExecutionBudgetResult<T>> RunAsync<T>(
        Func<CancellationToken, Task<T>> body,
        long? budgetMs,
        CancellationToken outer)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (budgetMs is not { } declared || declared <= 0)
        {
            return new ExecutionBudgetResult<T>(
                ExecutionBudgetOutcome.NotBudgeted,
                await body(outer).ConfigureAwait(false),
                null);
        }

        var run = await BoundedRun.RunAsync(
            body,
            TimeSpan.FromMilliseconds(declared),
            outer).ConfigureAwait(false);

        return run.Kind switch
        {
            BoundedRunOutcomeKind.Completed
                => new ExecutionBudgetResult<T>(ExecutionBudgetOutcome.Completed, run.Value, declared),
            BoundedRunOutcomeKind.TimedOut
                => new ExecutionBudgetResult<T>(ExecutionBudgetOutcome.TimedOut, default, declared),
            _ => new ExecutionBudgetResult<T>(ExecutionBudgetOutcome.OuterCanceled, default, declared)
        };
    }

    /// <summary>void 重载（结果占位 true）。</summary>
    public static Task<ExecutionBudgetResult<bool>> RunAsync(
        Func<CancellationToken, Task> body,
        long? budgetMs,
        CancellationToken outer)
        => RunAsync(
            async ct =>
            {
                await body(ct).ConfigureAwait(false);
                return true;
            },
            budgetMs,
            outer);
}
