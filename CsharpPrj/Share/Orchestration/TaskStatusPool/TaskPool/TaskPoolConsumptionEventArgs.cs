using System;
using System.Collections.Generic;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>消费循环阶段（触发链可观测域）：一次 drain 的结束（正常/故障）。</summary>
public enum TaskPoolConsumptionPhase
{
    /// <summary>一次 drain 正常结束（耗时/待办数/本次启动键均有效）。</summary>
    DrainEnd,

    /// <summary>一次 drain 以异常结束（自愈续跑前上报，reason 携带异常类型/消息）。</summary>
    DrainFault,
}

/// <summary>
/// 消费循环阶段观测（诊断）—— 任务池"唤醒→drain→启动"链的可观测出口。
/// 设计意图：任务池不得成为黑盒——「已入队却长时间 NotStarted」必须能被区分成
/// 「未收到唤醒」「drain 被阻塞」「拉取为空」三种可判定情形，而非只能靠事后猜测。
/// 宿主（Agent 组合根）订阅后落日志；池层保持 Agent 无关（仅委托/值传输）。
/// </summary>
public sealed class TaskPoolConsumptionEventArgs : EventArgs
{
    public TaskPoolConsumptionEventArgs(
        TaskPoolConsumptionPhase phase, long elapsedMs = 0, int pending = 0,
        string? taskKey = null, string? reason = null, int skipped = 0,
        IReadOnlyList<(string TaskKey, TaskExecutionStatus MemoryState)>? skippedRows = null)
    {
        Phase = phase;
        ElapsedMs = elapsedMs;
        Pending = pending;
        TaskKey = taskKey;
        Reason = reason;
        Skipped = skipped;
        SkippedRows = skippedRows;
    }

    /// <summary>阶段。</summary>
    public TaskPoolConsumptionPhase Phase { get; }

    /// <summary>本阶段耗时（毫秒）：drain「开始→结束」的真实耗时（阻塞诊断的关键量；异常路径同样上报）。</summary>
    public long ElapsedMs { get; }

    /// <summary>本次持久化拉取到的可消费（NotStarted ∧ Active）条目数。</summary>
    public int Pending { get; }

    /// <summary>本次启动的任务键（未启动为 null）。</summary>
    public string? TaskKey { get; }

    /// <summary>唤醒来源/备注（如 signal / reconcile）。</summary>
    public string? Reason { get; }

    /// <summary>本次被跳过的候选数（持久化行可消费但内存态已非 NotStarted 的陈旧/毒化行；跳过并已对齐持久化）。</summary>
    public int Skipped { get; }

    /// <summary>本次被跳过的陈旧/毒化行明细（任务键 + 内存真态）：供宿主逐键留痕「持久化行与内存态分叉已对齐」。
    /// 为 null/空 = 本次无陈旧行（Skipped 中仍可能含 <c>TryStart</c> 竞态跳过，二者语义不同故不合并）。</summary>
    public IReadOnlyList<(string TaskKey, TaskExecutionStatus MemoryState)>? SkippedRows { get; }

}
