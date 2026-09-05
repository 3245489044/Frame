#nullable enable

namespace FrontEndDriven.Shared.Framework;

/// <summary>
/// 队列排空共享策略（单点可调）：RenderRequestQueue 与 LayoutScheduler 共用的排空预算。
/// drain 骨架已由 <see cref="VersionedKeyedQueue{TKey,TEntry}.DrainAllAsync"/> 收敛，
/// 本策略承载两者排空的预算行为，杜绝各实现自行硬编码（治两层调度策略分散）。
/// </summary>
public static class QueueDrainPolicy
{
    /// <summary>单次 drain 最大迭代轮数（排空预算；LayoutScheduler / RenderRequestQueue 共用，原 MaxDrainPasses=32）。</summary>
    public const int MaxPassesPerDrain = 32;
}
