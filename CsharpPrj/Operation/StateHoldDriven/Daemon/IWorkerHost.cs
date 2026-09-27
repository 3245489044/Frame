using Modules.Threading;

namespace Operation.StateBlock;

/// <summary>
/// 传输层 worker 宿主抽象 —— 通用 daemon 池只依赖本契约，不感知具体工具（node daemon / ClineCore 等）。
/// 职责：spawn 进程、暴露进程句柄（供 AEF 看门狗 kill 整棵进程树）、定义就绪（warm）、发请求、阶段追踪、退出回调。
/// </summary>
public interface IWorkerHost : IDisposable
{
    /// <summary>worker 唯一 id（与池内状态/请求关联键一致）。</summary>
    string WorkerId { get; }

    /// <summary>底层进程句柄（供 AEF 在绝对超时/空闲挂起时 kill）。</summary>
    IProcessHandle ProcessHandle { get; }

    /// <summary>当前执行阶段/进度（trace 帧累计，卡点定位用）；无则 null。</summary>
    string? LastStage { get; }

    /// <summary>进程退出（被 kill/崩溃）时触发；池据此标记 Hung/Stopped 并补位。</summary>
    event Action? Exited;

    /// <summary>stderr 行（--trace / 诊断），转发池诊断日志，定位 daemon 卡点。</summary>
    event Action<string>? Stderr;

    /// <summary>阶段追踪帧（type:"trace"），池透传诊断。</summary>
    event Action<DaemonResponse>? Trace;

    /// <summary>非终结/非追踪类 Json-Line 帧（如工具私有帧 <c>type:"node"</c>）：池透传为 **per-call** 帧通道。
    /// 与 <see cref="Trace"/> 并列：Trace 承载阶段枚举，本事件承载任意结构载荷（Detail/Form）。
    /// 纪律：消费方须按 Type 判别并容错未知类型（禁把未知类型当语义判据）。</summary>
    event Action<DaemonResponse>? Frame;

    /// <summary>spawn 底层进程并接入传输。失败抛异常。</summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>轻量探活（ping/pong）。返回是否存活。</summary>
    Task<bool> ProbeAsync(TimeSpan? timeout, CancellationToken ct);

    /// <summary>发送业务请求并等待终结帧；可设超时与外部取消，进度经 onProgress 推进，ackTimeout 为 TTFR 首帧超时。</summary>
    Task<DaemonResponse> SendAsync(DaemonRequest request, TimeSpan? timeout, CancellationToken ct, Action? onProgress = null, TimeSpan? ackTimeout = null);
}
