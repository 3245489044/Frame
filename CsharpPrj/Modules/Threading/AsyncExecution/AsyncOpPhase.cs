
namespace Modules.Threading;

/// <summary>
/// 统一异步操作状态（扩展既有 <see cref="ExecutionPhase"/>，新增 IdleHung 区分"空闲挂起"与"绝对超时"）。
/// </summary>
public enum AsyncOpPhase
{
    Pending = 0,   // 已提交未调度
    Running = 1,   // 执行中（含首次启动初始化）
    Completed = 2, // 成功完成
    Failed = 3,    // 失败（返回结果或抛异常）
    Cancelled = 4, // 主动取消
    TimedOut = 5,  // 绝对超时（可选启用）
    IdleHung = 6   // 空闲挂起（心跳超窗）——本框架核心新增态
}

/// <summary>异步操作状态转移事件参数。</summary>
public sealed class AsyncOpPhaseChangedEventArgs : EventArgs
{
    public AsyncOpPhaseChangedEventArgs(AsyncOpPhase oldPhase, AsyncOpPhase newPhase)
    {
        OldPhase = oldPhase;
        NewPhase = newPhase;
    }

    public AsyncOpPhase OldPhase { get; }

    public AsyncOpPhase NewPhase { get; }

    public DateTime Timestamp { get; } = DateTime.UtcNow;
}
