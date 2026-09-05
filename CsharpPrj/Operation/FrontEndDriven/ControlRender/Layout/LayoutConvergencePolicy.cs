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

    /// <summary>Retrying 最大退避重试次数（取代原 LayoutScheduler.MaxBudgetReschedules=3 的"无条件重排"语义）。</summary>
    public const int RetryBudget = 3;

    /// <summary>递增退避序列（次）：1/2/4，超出预算返回 null（→ 转 Abandoned）。</summary>
    public static readonly int[] RetryBackoff = { 1, 2, 4 };

    /// <summary>连续无进展放弃阈值（超过 → Abandoned）。</summary>
    public const int AbandonThreshold = 3;

    /// <summary>兜底扫描间隔（毫秒，Step2）：主回路（Resize/Move）之外的周期性值差扫描频率。</summary>
    public const int GeometrySweepIntervalMs = 1000;

    /// <summary>下一次退避（attempt 为 0-based：0→1, 1→2, 2→4）；超出预算返回 null。</summary>
    public static int? NextBackoff(int attempt)
        => attempt >= 0 && attempt < RetryBackoff.Length ? RetryBackoff[attempt] : null;

    /// <summary>是否应放弃（连续无进展 ≥ AbandonThreshold）。</summary>
    public static bool ShouldAbandon(int consecutiveNoProgress)
        => consecutiveNoProgress >= AbandonThreshold;

    /// <summary>
    /// 宿主就绪分类（纯函数，无副作用）：决定 scope 进入 Blocked / Retrying / Ready。
    /// Step2：Form 与容器宿主一视同仁——窗体宿主也能随缩放/移动/DPI 触发反应式重排，
    /// 不再因「保持配置基准」被永久 Blocked（否则 Form 直挂 scope 永不自动刷新）。
    /// </summary>
    public static LayoutHostReadiness ClassifyHost(
        bool hostResolved,
        bool clientReady,
        bool parentClientReady,
        bool rootFound)
    {
        // 宿主未挂载（未解析/未注册）→ Blocked：等待挂载信号，不重试、不空转。
        if (!hostResolved)
            return LayoutHostReadiness.Blocked;

        // 宿主已挂载但基准/root 瞬态未就绪 → Retrying：有限递增退避重试。
        if (!clientReady && !parentClientReady)
            return LayoutHostReadiness.Retrying;
        if (!rootFound)
            return LayoutHostReadiness.Retrying;

        return LayoutHostReadiness.Ready;
    }
}
