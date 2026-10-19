using System;
using System.Collections.Generic;
using Modules.Threading;
using Share.TaskStatusPool.Loops;

namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 循环运行结果（终态单一载体）：状态 + 各轮产物 + 决策 + 理由 + 统一 Outcome + 证据。
/// 调用方据此判成功/失败，禁再从文本/空集合推断。
/// </summary>
public sealed record LoopRunResult<TState, TResult>(
    TState FinalState,
    IReadOnlyList<TResult> Rounds,
    LoopDecision Decision,
    TerminationReason Reason,
    LoopOutcome Outcome,
    string? Evidence,
    TimeSpan TotalDuration);
