
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FrameworkAbstractions.WinFormsAbstractions;
using Share.EventBindingOrchestrator.Execution;
using Share.EventBindingOrchestrator.Invocation;

namespace Share.EventBindingOrchestrator.Abstractions;

/// <summary>
/// 函数调用结果（外部契约）：<see cref="Output"/> 为文本投影（脚本/命令叙述），
/// <see cref="RawOutput"/> 保留原始输出对象（如 RenderOutput 落地计划，禁止在此层 string 化后丢弃）。
/// <see cref="Outcome"/> 给出框架级归因（函数错 vs 框架错），修复「门面边界压成同一 false」。
/// </summary>
public sealed record FunctionCallFacadeResult(
    bool IsSuccess,
    string? Output,
    string? ErrorMessage,
    long ElapsedMs,
    // 阶段2.2：函数渲染副作用元数据（随执行结果携带，供事件发布/渲染桥消费，见 RenderingAffinity）
    RenderingAffinity RenderingAffinity = RenderingAffinity.None,
    // E4 修复：保留内核终态分类，框架错（超时/空闲/信号丢失）与函数错在门面边界可区分。
    InvocationOutcome Outcome = InvocationOutcome.Completed,
    // D10 依赖：输出落地需原始对象（RenderOutput 计划），不得只剩 ToString 文本。
    object? RawOutput = null,
    Guid OpId = default);

public interface IEventPlatform
{
    Task<FunctionCallFacadeResult> InvokeFunctionAsync(
        string functionName,
        Dictionary<string, object?>? parameters = null,
        string? source = null,
        int timeoutMs = InteractionDefaults.DefaultFunctionTimeoutMs,
        string scope = "");

    FunctionCallFacadeResult InvokeFunction(
        string functionName,
        Dictionary<string, object?>? parameters = null,
        string? source = null,
        int timeoutMs = InteractionDefaults.DefaultFunctionTimeoutMs,
        string scope = "");
}
