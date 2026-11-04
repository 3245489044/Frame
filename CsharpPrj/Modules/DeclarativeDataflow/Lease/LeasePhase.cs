using System;

namespace Modules.DeclarativeDataflow.Lease;

/// <summary>
/// 挂载租约相位（**挂载语义专用受控词**，与运行态词表正交）。
/// <para>
/// 与 <see cref="RunState.RunStateStage"/> 的分工：本词表答"租约/挂载处于什么阶段"，
/// 运行态词表答"实体处于什么运行阶段"；跨词表一律经 <see cref="LeasePhaseStages.Map"/> 显式映射，
/// 禁在宿主侧散落字面量（否则形成第二套阶段词）。
/// </para>
/// </summary>
public enum LeasePhase
{
    /// <summary>未挂载（初始态；无租约）。</summary>
    Created = 0,

    /// <summary>挂载中（已取到目标，尚未确认可用）。</summary>
    Mounting = 1,

    /// <summary>已挂载待确认（渲染/校验完成，等待写计划采纳）。</summary>
    Mounted = 2,

    /// <summary>已打开（当前生效的租约）。</summary>
    Open = 3,

    /// <summary>已拒绝（本拍挂载失败；当前租约保持不变）。</summary>
    Rejected = 4,

    /// <summary>释放中（两段释放的第一段）。</summary>
    Releasing = 5,

    /// <summary>已释放（无租约；可再次挂载）。</summary>
    Released = 6,
}

/// <summary>租约动作（表驱动裁决的输入，非字符串）。</summary>
public enum LeaseAction
{
    /// <summary>申请挂载（可能覆盖待确认项）。</summary>
    Mount = 0,

    /// <summary>确认（待确认项转正为当前）。</summary>
    Confirm = 1,

    /// <summary>拒绝（丢弃待确认项；当前不推进）。</summary>
    Reject = 2,

    /// <summary>释放（按策略决定是否真正丢弃当前）。</summary>
    Release = 3,
}

/// <summary>释放策略（声明面给定；框架按表裁决）。</summary>
public enum LeasePolicy
{
    /// <summary>按需：离开即释放（当前行为基线）。</summary>
    OnDemand = 0,

    /// <summary>常驻：离开保留租约（DOM/状态由宿主按需处理）。</summary>
    Persistent = 1,
}
