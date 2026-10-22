#nullable enable

namespace FrontEndDriven.ControlRender.Layout;

/// <summary>布局收敛状态（确定性收敛状态机的四态）。</summary>
public enum LayoutConvergenceState
{
    /// <summary>本轮投影无实际变更，已收敛（不再入队）。</summary>
    Converged,

    /// <summary>瞬态未就绪（宿主已挂载但基准/root 暂未就位）——有限递增退避重试。</summary>
    Retrying,

    /// <summary>宿主未挂载——等待挂载信号（RenderMountStage → RegisterScope/ReflowNow 单次触发），不重试不空转。</summary>
    Blocked,

    /// <summary>结构非法 / 连续无进展——明确放弃并告警，不再 ScheduleDrain。</summary>
    Abandoned
}

/// <summary>宿主就绪分类（LayoutSubsystem.Reflow 门控的输入）。</summary>
public enum LayoutHostReadiness
{
    /// <summary>宿主已解析、非 Form、尺寸就绪、root 可达 → 允许重排。</summary>
    Ready,

    /// <summary>宿主未挂载（未解析/未注册）→ 等待挂载信号，不入队不空转。</summary>
    Blocked,

    /// <summary>宿主已挂载但基准瞬态未就绪（0x0 / root 暂未找）→ 有限退避重试。</summary>
    Retrying
}

/// <summary>
/// 布局收敛策略（确定性收敛状态机的阈值与判定单源）。
/// 收敛 LayoutSystem / LayoutProjector / LayoutScheduler 的散落魔数，
/// 并定义四态（Converged/Retrying/Blocked/Abandoned）的纯函数判定。
/// </summary>
public static class LayoutConvergencePolicy
{
    // ── 阈值单源：收敛 5 个散落魔数 ──
    /// <summary>LayoutSystem.Update 收敛循环上限（原 LayoutSystem.MaxLayoutPasses=16）。</summary>
    public const int MaxLayoutPasses = 16;

    /// <summary>LayoutProjector.ProjectScope 收敛循环上限（原 LayoutProjector.MaxProjectionPasses=8）。</summary>
    public const int MaxProjectionPasses = 8;

    /// <summary>单次 drain 内同一 scope 连续重排风暴阈值（原 LayoutScheduler.ReflowStormThreshold=16）。</summary>
    public const int ReflowStormThreshold = 16;

    /// <summary>重排退避预算（收敛机对象 = 宿主未就绪次数；与调度 drain 预算分离）。</summary>
    public const int ReflowRetryBudget = 3;

    /// <summary>调度排空预算（LayoutScheduler 对象 = drain 后仍滞留次数；与重排退避分离）。</summary>
    public const int DrainBudget = 3;

    /// <summary>递增退避序列（次）：1/2/4；延时提供方，不承担终止判定。</summary>
    public static readonly int[] RetryBackoff = { 1, 2, 4 };

    /// <summary>兜底扫描间隔（毫秒，Step2）：主回路（Resize/Move）之外的周期性值差扫描频率。</summary>
    public const int GeometrySweepIntervalMs = 1000;

    /// <summary>下一次退避（attempt 为 0-based：0→1, 1→2, 2→4）；超出预算返回 null。</summary>
    public static int? NextBackoff(int attempt)
        => attempt >= 0 && attempt < RetryBackoff.Length ? RetryBackoff[attempt] : null;

    /// <summary>
    /// 唯一放弃判据（attempt = 已消耗退避次数）：达到 <see cref="ReflowRetryBudget"/> ⇒ 放弃。
    /// <see cref="NextBackoff"/> 仅提供延时，不再兼任终止判定（消除双阈值）。
    /// </summary>
    public static bool ShouldAbandon(int attempt)
        => attempt >= ReflowRetryBudget;
}
