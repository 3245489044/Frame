using System;
using Modules.DeclarativeDataflow.RunState;
using Share.HierarchySupport;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.RunState;

/// <summary>
/// 运行态阶段词表（内核 <see cref="RunStateStage"/>）与运行快照发布 stage
/// （<see cref="RuntimeStage"/>）之间的**跨程序集桥**。
/// <para>
/// 归属说明：内核只承载中性受控词，不引用 <c>Share.HierarchySupport</c>（守零项目引用）；
/// 两个词表的映射是"依赖方对目标词表的知识"，故留在域侧，**不是兼容 shim**。
/// </para>
/// <para>调用方（宿主循环 / 快照发布）一律经本桥显式映射，禁各自散落字符串字面量。</para>
/// </summary>
public static class RunStateStageBridge
{
    /// <summary>运行快照 stage（<see cref="RuntimeStage"/>）→ 受控阶段。</summary>
    public static string FromRuntimeStage(string? stage) => stage?.Trim().ToLowerInvariant() switch
    {
        RuntimeStage.Started => RunStateStage.Started,
        RuntimeStage.Running => RunStateStage.Running,
        RuntimeStage.Paused => RunStateStage.Paused,
        RuntimeStage.Completed => RunStateStage.Completed,
        _ => RunStateStage.Idle,
    };

    /// <summary>
    /// 受控阶段 → 运行快照 stage（<see cref="RuntimeStage"/>）；失败/取消按
    /// <see cref="RuntimeStage.FromStatus"/> 的既有口径归 <see cref="RuntimeStage.Idle"/>（不产生歧义帧）。
    /// </summary>
    public static string ToRuntimeStage(string? stage) => stage?.Trim().ToLowerInvariant() switch
    {
        RunStateStage.Started => RuntimeStage.Started,
        RunStateStage.Running => RuntimeStage.Running,
        RunStateStage.Paused => RuntimeStage.Paused,
        RunStateStage.Completed => RuntimeStage.Completed,
        _ => RuntimeStage.Idle,
    };
}
