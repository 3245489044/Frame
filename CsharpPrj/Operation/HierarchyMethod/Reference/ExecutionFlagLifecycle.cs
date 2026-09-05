namespace Operation.HierarchyMethod.Reference;

/// <summary>
/// 执行标识符反应式生命周期状态（设计 §5）：Idle ──SetFlag──▶ Flagged ──Resolve──▶ Routed ──Run──▶ Running ──完成──▶ Completed ──Reset──▶ Idle(Std)。
/// 路由失败 → Faulted；取消 → Cancelled；Reset 无条件把 exec.flag 置 Std、lifecycle 置 Idle（回稳态，等待下一次 SetFlag）。
/// </summary>
public enum FlagLifecycleState
{
    /// <summary>标准态（等待下一次 SetFlag）。</summary>
    Idle,

    /// <summary>已 SetFlag（写 exec.flag + lifecycle=flagged），待解析生效标志。</summary>
    Flagged,

    /// <summary>已沿引用边解析出实际执行模式，待路由。</summary>
    Routed,

    /// <summary>已路由到已适配框架，执行中。</summary>
    Running,

    /// <summary>运行完成。</summary>
    Completed,

    /// <summary>路由/运行失败。</summary>
    Faulted,

    /// <summary>取消。</summary>
    Cancelled,
}

/// <summary>状态机触发事件（<see cref="ExecutionFlagLifecycle.Transition"/> 的入参）。</summary>
public enum FlagLifecycleTrigger
{
    /// <summary>设置/切换标识符。</summary>
    SetFlag,

    /// <summary>解析生效标志。</summary>
    Resolve,

    /// <summary>路由到已适配框架。</summary>
    Route,

    /// <summary>运行。</summary>
    Run,

    /// <summary>运行完成。</summary>
    Complete,

    /// <summary>运行失败。</summary>
    Fail,

    /// <summary>取消。</summary>
    Cancel,

    /// <summary>回切标准标识符（Reset）。</summary>
    Reset,
}

/// <summary>
/// 反应式生命周期状态机（纯函数，可测）：给定当前状态 + 触发事件 → 下一状态；非法转移返回 null（调用方视为无效跃迁）。
/// 权威设计：执行标识符框架设计 §5（Idle→Flagged→Routed→Running→Completed→Reset）。
/// </summary>
public static class ExecutionFlagLifecycle
{
    /// <summary>执行状态转移；非法转移返回 null（不可跃迁）。</summary>
    public static FlagLifecycleState? Transition(FlagLifecycleState from, FlagLifecycleTrigger trigger)
        => (from, trigger) switch
        {
            (FlagLifecycleState.Idle, FlagLifecycleTrigger.SetFlag) => FlagLifecycleState.Flagged,

            (FlagLifecycleState.Flagged, FlagLifecycleTrigger.Resolve) => FlagLifecycleState.Routed,
            (FlagLifecycleState.Flagged, FlagLifecycleTrigger.Fail) => FlagLifecycleState.Faulted,

            (FlagLifecycleState.Routed, FlagLifecycleTrigger.Run) => FlagLifecycleState.Running,
            (FlagLifecycleState.Routed, FlagLifecycleTrigger.Fail) => FlagLifecycleState.Faulted,

            (FlagLifecycleState.Running, FlagLifecycleTrigger.Complete) => FlagLifecycleState.Completed,
            (FlagLifecycleState.Running, FlagLifecycleTrigger.Fail) => FlagLifecycleState.Faulted,
            (FlagLifecycleState.Running, FlagLifecycleTrigger.Cancel) => FlagLifecycleState.Cancelled,

            // Reset：回切标准态（completed/faulted/cancelled → Idle(Std)）。
            (FlagLifecycleState.Completed, FlagLifecycleTrigger.Reset) => FlagLifecycleState.Idle,
            (FlagLifecycleState.Faulted, FlagLifecycleTrigger.Reset) => FlagLifecycleState.Idle,
            (FlagLifecycleState.Cancelled, FlagLifecycleTrigger.Reset) => FlagLifecycleState.Idle,

            _ => null,
        };

    /// <summary>状态 → 元数据落值字符串。</summary>
    public static string ToString(FlagLifecycleState state)
        => state.ToString().ToLowerInvariant();
}
