namespace Modules.Threading;

/// <summary>
/// 命名时间预算的**单一来源**。各模块不再各自写死 30s/120s/90s/60s 等常量，
/// 统一引用本表；调整一处即全局生效。
/// </summary>
public static class TimingDefaults
{
    // —— 调度 / 空闲看门狗 ——
    /// <summary>AEF 空闲看门狗默认窗口（心跳超窗即判挂起）。</summary>
    public static TimeSpan AefIdleWindow { get; } = TimeSpan.FromSeconds(30);

    /// <summary>空闲看门狗默认轮询周期。</summary>
    public static TimeSpan AefPollInterval { get; } = TimeSpan.FromSeconds(1);

    // —— 通用命令 / 管线 ——
    /// <summary>管线阶段默认超时（对齐 WorkflowExecution.Node 的 30000ms）。</summary>
    public static TimeSpan PipelineStage { get; } = TimeSpan.FromSeconds(30);

    /// <summary>UI 等待命令默认超时。</summary>
    public static TimeSpan UiWaitCommand { get; } = TimeSpan.FromSeconds(30);

    /// <summary>函数/命令调用默认超时（对齐 EventPlatformExecutor 的 30_000ms 单一来源）。</summary>
    public static TimeSpan FunctionCallDefault { get; } = TimeSpan.FromSeconds(30);

    // —— Cline 链路（消除 120s/90s/60s/20min 重复；Phase 1 由 ClineDaemonOptions 引用）——
    /// <summary>Cline 池预热预算（覆盖 56s 冷启动 + 波动；留足冷机/首次初始化余量）。</summary>
    public static TimeSpan ClineWarmup { get; } = TimeSpan.FromSeconds(180);

    /// <summary>Cline 单请求绝对超时兜底。</summary>
    public static TimeSpan ClineRequest { get; } = TimeSpan.FromMinutes(20);
    /// <summary>Cline 单次调用运行上限（每次 cline_execute/AskAsync 的硬顶；调用方设置 TimeoutSec 的上界，而非池层 20 分钟兜底）。</summary>
    public static TimeSpan ClineCallCap { get; } = TimeSpan.FromMinutes(5);

    /// <summary>Cline 空闲看门狗窗口（&gt;10s 心跳余量）。</summary>
    public static TimeSpan ClineIdle { get; } = TimeSpan.FromSeconds(60);

    /// <summary>Cline 预热空闲窗口（覆盖 56s 冷启动；被 ClineCore 阻塞不发自保 stage 时判挂起）。</summary>
    public static TimeSpan ClineWarmupIdle { get; } = TimeSpan.FromSeconds(90);

    /// <summary>Cline 预算余量（内层自报超时 = deadline - margin）。</summary>
    public static TimeSpan ClineBudgetMargin { get; } = TimeSpan.FromSeconds(5);

    /// <summary>Cline ping/pong 探活超时（S1：探活不再无超时兜底，避免 StartAsync 在探活处无限挂起）。</summary>
    public static TimeSpan ClineProbe { get; } = TimeSpan.FromSeconds(10);

    /// <summary>Cline 管理命令（history 等）显式预算。</summary>
    public static TimeSpan ClineHistory { get; } = TimeSpan.FromSeconds(60);

    // —— 卡死取证（Hang Diagnostics，慢路径进度心跳）——
    /// <summary>慢路径心跳停更默认阈值：<see cref="HangProbe"/> 的 Tick 超过此未更新即判卡死候选。</summary>
    public static TimeSpan HangStall { get; } = TimeSpan.FromSeconds(30);

    /// <summary>卡死取证最近日志环默认容量（仅在取证瞬间读取）。</summary>
    public static int HangLogRingCapacity { get; } = 200;
}
