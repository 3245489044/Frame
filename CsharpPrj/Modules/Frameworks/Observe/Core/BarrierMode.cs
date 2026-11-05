namespace Modules.Observe;

/// <summary>屏障策略（写方可选）：Strict=必须收到 Converged 才返回成功；Timeout=界内未收敛则返回 Degraded；FireAndForget=立即返回，收敛由回调/事件通知。</summary>
public enum BarrierMode
{
    Strict,
    Timeout,
    FireAndForget
}
