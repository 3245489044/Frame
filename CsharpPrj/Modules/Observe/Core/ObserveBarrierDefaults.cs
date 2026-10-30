#nullable enable

namespace Modules.Observe;

/// <summary>
/// 写屏障常量（单一来源）——「写回来源 → 等待窗口」的**取值**归本处；
/// 来源到屏障模式的映射函数留在连接层（HTML 写回驱动），观测域不引用渲染/传输类型。
/// </summary>
public static class ObserveBarrierDefaults
{
    /// <summary>交互/流来源：不等待屏障（FireAndForget）。</summary>
    public const int FireAndForgetWindowMs = 0;

    /// <summary>刷新来源窗口（毫秒）：交互已走直写，刷新无需长窗。</summary>
    public const int RefreshWindowMs = 250;

    /// <summary>挂载/其它来源窗口（秒）。</summary>
    public const int MountWindowSeconds = 5;
}
