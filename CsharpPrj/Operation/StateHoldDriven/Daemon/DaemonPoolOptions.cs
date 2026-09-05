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

    /// <summary>池内基建故障重试上限（换 worker/补位）。</summary>
    public int InfraRetryLimit { get; set; } = 1;
}
