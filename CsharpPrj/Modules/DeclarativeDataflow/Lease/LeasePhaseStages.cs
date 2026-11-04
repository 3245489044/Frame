using System;

namespace Modules.DeclarativeDataflow.Lease;

/// <summary>
/// 租约相位 → 运行态受控词的**唯一映射点**（表驱动，无分支梯）。
/// <para>
/// 纪律：宿主侧禁写 <c>"open"</c>/<c>"rejected"</c> 等租约字面量；一切展示/落库都经本表落回
/// <see cref="RunState.RunStateStage"/> 既有受控词（避免第二套阶段词）。
/// </para>
/// </summary>
public static class LeasePhaseStages
{
    /// <summary>映射为运行态受控词。</summary>
    public static string Map(LeasePhase phase) => phase switch
    {
        LeasePhase.Created => RunState.RunStateStage.Idle,
        LeasePhase.Mounting => RunState.RunStateStage.Started,
        LeasePhase.Mounted => RunState.RunStateStage.Started,
        LeasePhase.Open => RunState.RunStateStage.Running,
        LeasePhase.Rejected => RunState.RunStateStage.Failed,
        LeasePhase.Releasing => RunState.RunStateStage.Running,
        LeasePhase.Released => RunState.RunStateStage.Completed,
        _ => RunState.RunStateStage.Idle,
    };
}
