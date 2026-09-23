using System;
using System.Collections.Generic;
using Share.HierarchySupport;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.RunState;

/// <summary>
/// 运行态阶段受控词表（**唯一来源**，SingleImplementation）—— 「层级」与「运行态」是正交两轴：
/// <see cref="Modules.DeclarativeDataflow.Model.LevelRef"/> 答"在哪一层哪个实体"，本词表答"处于什么阶段"。
/// <para>
/// 与 <see cref="RuntimeStage"/> 的分工（不重复、不分裂）：后者是**运行快照发布** stage 的受控来源
/// （AgentRunner / AgentTaskPool 落库发布用，仅 started/running/completed/paused/idle 五词）；
/// 本词表是**运行态投影/展示** stage 的受控来源（额外区分 Failed/Cancelled 终态），
/// 二者经 <see cref="FromRuntimeStage"/>/<see cref="ToRuntimeStage"/> 显式映射，禁各自散落字符串字面量。
/// </para>
/// <para>
/// 本词表**不含任何执行体语义**（无 agent/cline/answer）——执行体是 <see cref="ActorRef"/> 维度，
/// 与阶段正交；故"某层是否由子智能体驱动"是数据事实而非本词表的假设。
/// </para>
/// </summary>
public static class RunStateStage
{
    /// <summary>未运行 / 未知（不猜阶段）。</summary>
    public const string Idle = "idle";

    /// <summary>已启动边界（尚未进入轮次）。</summary>
    public const string Started = "started";

    /// <summary>执行中（含轮次推进与恢复）。</summary>
    public const string Running = "running";

    /// <summary>挂起（非终止，可恢复）。</summary>
    public const string Paused = "paused";

    /// <summary>成功终态。</summary>
    public const string Completed = "completed";

    /// <summary>失败终态。</summary>
    public const string Failed = "failed";

    /// <summary>取消终态（终止域，区别于 <see cref="Idle"/> 未开始）。</summary>
    public const string Cancelled = "cancelled";

    /// <summary>全部受控词（供枚举 / 校验 / 模板样式映射）。</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        Idle, Started, Running, Paused, Completed, Failed, Cancelled,
    };

    /// <summary>是否终态（<see cref="Completed"/>/<see cref="Failed"/>/<see cref="Cancelled"/>）。</summary>
    public static bool IsTerminal(string? stage)
        => Equals(stage, Completed) || Equals(stage, Failed) || Equals(stage, Cancelled);

    /// <summary>是否进行中（<see cref="Started"/>/<see cref="Running"/>）。</summary>
    public static bool IsActive(string? stage) => Equals(stage, Started) || Equals(stage, Running);

    /// <summary>是否受控词（未知文本 → false，供写入侧校验）。</summary>
    public static bool IsKnown(string? stage)
        => !string.IsNullOrWhiteSpace(stage) && All.Contains(stage!.Trim().ToLowerInvariant());

    /// <summary>
    /// 任务生命周期 / 执行器状态文本 → 受控阶段（表驱动，唯一映射点）。
    /// 未知 / 空白 → <see cref="Idle"/>（**不猜 running**：无证据的运行态就是未运行）。
    /// </summary>
    public static string FromStatusText(string? statusText) => statusText?.Trim().ToLowerInvariant() switch
    {
        "started" => Started,
        "running" => Running,
        "paused" => Paused,
        "completed" or "done" => Completed,
        "faulted" or "error" or "failed" => Failed,
        "cancelled" or "canceled" => Cancelled,
        _ => Idle,
    };

    /// <summary>运行快照 stage（<see cref="RuntimeStage"/>）→ 受控阶段。</summary>
    public static string FromRuntimeStage(string? stage) => stage?.Trim().ToLowerInvariant() switch
    {
        RuntimeStage.Started => Started,
        RuntimeStage.Running => Running,
        RuntimeStage.Paused => Paused,
        RuntimeStage.Completed => Completed,
        _ => Idle,
    };

    /// <summary>
    /// 受控阶段 → 运行快照 stage（<see cref="RuntimeStage"/>）；失败/取消按
    /// <see cref="RuntimeStage.FromStatus"/> 的既有口径归 <see cref="RuntimeStage.Idle"/>（不产生歧义帧）。
    /// </summary>
    public static string ToRuntimeStage(string? stage) => stage?.Trim().ToLowerInvariant() switch
    {
        Started => RuntimeStage.Started,
        Running => RuntimeStage.Running,
        Paused => RuntimeStage.Paused,
        Completed => RuntimeStage.Completed,
        _ => RuntimeStage.Idle,
    };

    /// <summary>归一任意 stage 文本到受控词（已受控原样返回；否则经状态文本表映射）。</summary>
    public static string Normalize(string? stage)
        => IsKnown(stage) ? stage!.Trim().ToLowerInvariant() : FromStatusText(stage);

    private static bool Equals(string? a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
