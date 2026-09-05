#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using FrameworkAbstractions.WinFormsAbstractions;

namespace Share.EventBindingOrchestrator.Invocation;

/// <summary>函数调用来源（入口收敛：所有入口最终都归一到内核）。</summary>
public enum InvocationSource
{
    Unknown,
    ButtonClick,
    CommandClick,
    ExecCommand,
    InitCommand,
    Workflow,
    ExternalPort,
    MatrixSink
}

/// <summary>
/// 调用终态分类（框架级归因裁决）——回答「卡死是函数错还是框架错」。
/// 该分类是唯一裁决点：函数错（FunctionFault）与框架错（Framework*/SignalLost）在失败消息上一眼可辨。
/// </summary>
public enum InvocationOutcome
{
    /// <summary>函数正常完成（IsSuccess 区分有/无输出）。</summary>
    Completed,

    /// <summary>函数自身失败（返回失败/抛异常/输出非法）→ 责任在函数。</summary>
    FunctionFault,

    /// <summary>框架绝对超时（Absolute 预算到期）→ 责任在框架执行层。</summary>
    FrameworkTimedOut,

    /// <summary>框架空闲挂起（预算窗口内无心跳）→ 责任在框架执行层。</summary>
    FrameworkIdleHung,

    /// <summary>主动/外部取消。</summary>
    Cancelled,

    /// <summary>完成信号未在预算+宽限内落定 → 框架缺陷（明确可判）。</summary>
    SignalLost
}

/// <summary>
/// 调用计时预算（单一权威计时：仅内核/调度器裁决；调用方不再起独立竞速时钟）。
/// <c>AbsoluteMs</c> 为墙钟预算；<c>IdleMs</c> 为无进展窗口（依赖心跳，无心跳源则不启用）。
/// </summary>
public readonly record struct InvocationBudget(int? AbsoluteMs = null, int? IdleMs = null)
{
    /// <summary>默认 30s 绝对预算、不启用空闲看门狗（空闲语义须有心跳源才开启）。权威常量见 <see cref="InteractionDefaults"/>。</summary>
    public static InvocationBudget Default { get; } = new(AbsoluteMs: InteractionDefaults.DefaultFunctionTimeoutMs, IdleMs: null);
}

/// <summary>分阶段计时（观测：一眼定位卡在哪个阶段）。</summary>
public sealed record InvocationStage(string Name, long ElapsedMs);

/// <summary>
/// 调用请求（内核唯一入口契约）。
/// </summary>
public sealed record InvocationRequest(
    string FunctionName,
    IReadOnlyDictionary<string, object?> Parameters,
    InvocationSource Source,
    InvocationBudget Budget,
    string? OutputTarget = null,
    object? ReentryKey = null,
    string? IsolationKey = null,
    Guid? CorrelationId = null,
    /// <summary>外部取消令牌（可选）：命令层等待超时可经此提前终止内核 op，与绝对预算构成双保险（Fix·双时钟对齐）。</summary>
    CancellationToken? CallerToken = null);

/// <summary>
/// 调用结果（内核唯一结果契约；取代 FunctionCallFacadeResult / AsyncOperationResult 作为对外口径）。
/// </summary>
public sealed record FunctionInvocationResult(
    Guid OpId,
    InvocationOutcome Outcome,
    bool IsSuccess,
    object? Output,
    string? Error,
    long ElapsedMs,
    IReadOnlyList<InvocationStage>? Stages = null,
    string? FunctionName = null)
{
    /// <summary>终态一行式（观测：grep 单个 opId 即可判定责任方）。</summary>
    public string ToTraceLine() =>
        $"Invocation.End {{ opId={OpId}, fn={FunctionName ?? "-"}, outcome={Outcome}, " +
        $"success={IsSuccess}, elapsedMs={ElapsedMs}, error={(Error ?? "-")} }}";
}

/// <summary>
/// 调用上下文（适配器与内核的契约面）：暴露取消令牌、心跳、进程注册。
/// 适配器经此向内核反馈真实进展并登记可终止的子进程，使看门狗不再对底层进程失明。
/// </summary>
public interface IInvocationContext
{
    /// <summary>取消贯穿令牌（函数支持取消时务必贯穿到最底层）。</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>报告进展（心跳）：任何「仍在推进」的证据都应调用，喂给空闲看门狗。</summary>
    void ReportProgress();

    /// <summary>登记可终止的子进程句柄（框架在终结时 kill，杜绝孤儿进程）。</summary>
    void RegisterProcess(Modules.Threading.IProcessHandle process);

    /// <summary>登记可终止的 System.Diagnostics.Process（内部包装为句柄）。</summary>
    void RegisterProcess(System.Diagnostics.Process process);
}

/// <summary>
/// 调用适配器（把「如何执行一个函数」封装为策略）：
/// 直接委托 / 工作流编排 / 子进程调用各自实现，统一接受内核上下文。
/// </summary>
public interface IInvocationAdapter
{
    /// <summary>是否支持取消贯穿（函数接收 CancellationToken）。</summary>
    bool SupportsCancellation { get; }

    /// <summary>执行函数，返回规范化输出（object?）。异常/失败须抛异常或返回失败标记。</summary>
    Task<object?> InvokeAsync(InvocationRequest request, IInvocationContext ctx);
}
