using System;
using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.Loops;

/// <summary>快照驱动循环的单轮产物。</summary>
public sealed class SnapshotLoopRound<TResult>
{
    /// <summary>轮次序号（从 1 起）。</summary>
    public int Index { get; init; }

    /// <summary>单轮执行体产物。</summary>
    public TResult Result { get; init; } = default!;
}

/// <summary>快照驱动循环运行结果。</summary>
public sealed class SnapshotLoopResult<TState, TResult>
{
    /// <summary>最终状态（整体快照）。</summary>
    public TState FinalState { get; set; } = default!;

    /// <summary>各轮产物（只增）。</summary>
    public List<SnapshotLoopRound<TResult>> Rounds { get; } = new();

    /// <summary>是否由探索终止（取消令牌 / externalTerminate 谓词）触发结束。</summary>
    public bool TerminatedByExternal { get; set; }

    /// <summary>是否由预算终止（<c>budgetTerminate</c> 决策）触发结束。权威: AX.TerminationBipartition.Budgetary。</summary>
    public bool TerminatedByBudget { get; set; }

    /// <summary>预算终止决策值（引擎内裁决，TS/TO/TM）。非预算终止（探索/取消）时保持 Continue。权威: AX.DecisionOutputSet。</summary>
    public LoopDecision FinalDecision { get; set; } = LoopDecision.Continue;

    public TimeSpan TotalDuration { get; set; }
}

/// <summary>
/// 快照驱动循环引擎（轮次式快照循环；与 <see cref="Share.TaskStatusPool.TaskPool.TaskPool{TItem,TResult}"/> 同属
/// Share.TaskStatusPool 的循环机制 canonical —— 本类型为「轮次式快照循环」，TaskPool 为「条目式并发任务循环」）。
/// 每轮：读当前快照 → execute（单轮执行体，如五步骨架）→ next（混合机制：读快照+数据，LLM+计算变更整体状态）→ 更新快照。
/// 与 Agent / 状态管理无关（自包含；决策域与引擎同库 —— 统一上下文原语 LoopDecision 等一并下沉至 Share.TaskStatusPool.Loops）。
/// 终止二分（AX.TerminationBipartition）：
///   · 预算终止（可判定，引擎内）：<paramref name="budgetTerminate"/> 返回 LoopDecision 终止值（TS/TO/TM）→ 引擎内 break，
///     阈值到达即触发，不外置；决策值由引擎承载到 <see cref="SnapshotLoopResult{TState,TResult}.FinalDecision"/>。
///   · 探索终止（不可判定，外置）：取消令牌 / <paramref name="externalTerminate"/> 谓词（宿主 watchdog）→ 停（AX-CORE.EssentiallyUndecidable）。
/// </summary>
public sealed class SnapshotDrivenLoopEngine<TState, TResult>
{
    /// <summary>
    /// 运行快照驱动循环。
    /// </summary>
    /// <param name="initialState">初始整体快照。</param>
    /// <param name="execute">单轮执行体：由当前快照执行一轮动作并返回产物。</param>
    /// <param name="next">状态转移函数（混合机制核心）：读当前快照 + 本轮产物 + 数据 → 计算/决策 → 返回下一整体快照。</param>
    /// <param name="ct">取消令牌（探索终止信号之一）。</param>
    /// <param name="externalTerminate">探索终止谓词（宿主级 watchdog/观察者；默认 null = 仅取消令牌可停）。</param>
    /// <param name="budgetTerminate">预算终止决策谓词（引擎内，可判定；默认 null = 不内置预算终止）。
    /// 返回 LoopDecision 终止值（Success/Timeout/TripTerminated）→ 引擎在循环头 break，阈值到达即触发，不外置；
    /// 返回 Continue/None/SwitchStrategy → 继续；决策值由引擎承载到 <see cref="SnapshotLoopResult{TState,TResult}.FinalDecision"/>。</param>
    public async Task<SnapshotLoopResult<TState, TResult>> RunAsync(
        TState initialState,
        Func<TState, CancellationToken, Task<TResult>> execute,
        Func<TState, TResult, CancellationToken, Task<TState>> next,
        CancellationToken ct = default,
        Func<TState, CancellationToken, Task<bool>>? externalTerminate = null,
        Func<TState, LoopDecision>? budgetTerminate = null)
    {
        PortGuard.Required(execute, nameof(execute));
        PortGuard.Required(next, nameof(next));

        var state = initialState;
        var result = new SnapshotLoopResult<TState, TResult> { FinalState = state };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var index = 0;
            while (true)
            {
                // 探索终止（外置，不可判定）：取消令牌 / 宿主 watchdog 谓词。
                if (ct.IsCancellationRequested || (externalTerminate != null && await externalTerminate(state, ct).ConfigureAwait(false)))
                {
                    result.TerminatedByExternal = true;
                    result.FinalState = state;
                    break;
                }
                // 注：取消已在上述分支短路处理（break + TerminatedByExternal），此处不再重复 ThrowIfCancellationRequested（R2），
                // 消除冗余双检查（原 break/throw 两语义等价且重复）。

                // 预算终止（引擎内，可判定，阈值到达即触发）：决策谓词返回终止值（TS/TO/TM）→ break，不外置（AX.TerminationBipartition.Budgetary）。
                // 决策值由引擎承载到 FinalDecision（决策域与引擎同库回归，无需消费方闭包记录）。
                if (budgetTerminate != null)
                {
                    var decision = budgetTerminate(state);
                    if (DecisionTerminals.IsTerminal(decision))
                    {
                        result.TerminatedByBudget = true;
                        result.FinalDecision = decision;
                        result.FinalState = state;
                        break;
                    }
                }

                var roundResult = await execute(state, ct).ConfigureAwait(false);
                index++;
                result.Rounds.Add(new SnapshotLoopRound<TResult> { Index = index, Result = roundResult });

                // 混合机制核心：读快照 + 本轮产物 → LLM 提议 + 计算校验 → 变更整体状态 → 下一快照。
                state = await next(state, roundResult, ct).ConfigureAwait(false);
                result.FinalState = state;
            }
        }
        catch (OperationCanceledException)
        {
            result.TerminatedByExternal = true;
            result.FinalState = state;
        }
        finally
        {
            stopwatch.Stop();
            result.TotalDuration = stopwatch.Elapsed;
        }

        return result;
    }
}
