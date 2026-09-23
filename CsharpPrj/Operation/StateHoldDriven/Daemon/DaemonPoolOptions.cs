namespace Operation.StateBlock;

/// <summary>
/// 通用 daemon 池配置（与具体工具无关）。宿主工具按需填值；缺省为中性兜底。
/// </summary>
public sealed class DaemonPoolOptions
{
    /// <summary>池大小（常驻 daemon 数）。</summary>
    public int PoolSize { get; set; } = 3;

    /// <summary>未显式传预算时单请求的绝对超时兜底（唯一默认时钟）。</summary>
    public TimeSpan AbsoluteTimeout { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>空闲看门狗窗口（AEF IdleTimeout，须大于心跳余量，避免长任务被误杀）。</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>ping/pong 探活超时。</summary>
    public TimeSpan? ProbeTimeout { get; set; }

    /// <summary>池级就绪预算（就绪门闩等待窗口，覆盖冷启动）。</summary>
    public TimeSpan WarmupTimeout { get; set; } = TimeSpan.FromSeconds(180);

    /// <summary>池内基建故障重试上限（换 worker/补位）。**仅作为策略输入**（<see cref="InfraRetryPolicy"/> 的
    /// <c>InfraRetryLimit</c> 字段）；缺省策略（未注入）⇒ 不重发，直接报错（禁无证据整轮重放，见施工单 §13）。</summary>
    public int InfraRetryLimit { get; set; } = 1;

    /// <summary>
    /// 基建故障「是否允许重发」策略（宿主注入；缺省 null ⇒ **一律不重发**，直接以归因异常终止）。
    /// <para>纪律（施工单 §13「重复跑一律报错」）：无续跑证据的整轮重发 = 重复执行同一 unit ⇒ 报错；
    /// 仅「同一 unit 首次瞬态（尚无持久化证据）」或「携带续跑证据（<c>resumeFromSeq</c>）」允许重发。</para>
    /// <para>机制留宿主：池只问策略，不解释 payload 键、不写治理判据（分支在宿主规则表侧投影）。</para>
    /// </summary>
    public Func<DaemonInfraRetryContext, DaemonInfraRetryDecision>? InfraRetryPolicy { get; set; }

    /// <summary>池语义名（可选；与建池序号合成日志池标识 "名#序号"，供同一进程多池并存时日志归属消歧）。
    /// 缺省 null → 标识退化为 "pool#序号"。</summary>
    public string? PoolName { get; set; }
}
