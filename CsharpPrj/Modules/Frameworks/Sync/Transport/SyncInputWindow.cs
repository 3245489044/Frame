using Modules.Sync.Contracts;

namespace Modules.Sync.Transport;

/// <summary>
/// 输入窗口 <c>[current, current + InputWindowTicks]</c> 三段语义的唯一实现：
/// <list type="bullet">
/// <item><description>小于 current → <see cref="RejectReason.Late"/>（太旧丢弃）</description></item>
/// <item><description>窗口内 → <c>null</c>（接受；未来 tick 由排序机排队等待）</description></item>
/// <item><description>大于 current + window → <see cref="RejectReason.Future"/>（超前瞻丢弃）</description></item>
/// </list>
/// <para>窗口宽度直接取契约 <see cref="SyncProtocol.InputWindowTicks"/>，不另设常量。</para>
/// <para>单线程模型：与 <see cref="SyncServerSession"/> 同线程独占，不内部加锁。</para>
/// </summary>
public sealed class SyncInputWindow
{
    private long _currentTick;

    /// <param name="initialTick">初始决策 tick（决策 tick 从 0 起）。</param>
    public SyncInputWindow(long initialTick)
    {
        if (initialTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialTick), "决策 tick 不能为负。");
        }

        _currentTick = initialTick;
    }

    /// <summary>窗口宽度（含端点）。</summary>
    public int WindowTicks => SyncProtocol.InputWindowTicks;

    /// <summary>当前决策 tick。</summary>
    public long CurrentTick => _currentTick;

    /// <summary>窗口上界（含）：<c>current + InputWindowTicks</c>。</summary>
    public long UpperBound => _currentTick + SyncProtocol.InputWindowTicks;

    /// <summary>推进当前 tick；只允许单调不减，回退视为编程错误而非协议事件。</summary>
    public void AdvanceTo(long tick)
    {
        if (tick < _currentTick)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), "当前 tick 只能单调推进。");
        }

        _currentTick = tick;
    }

    /// <summary>判定一条输入的目标 tick：返回拒绝原因，<c>null</c> 表示窗口内接受。</summary>
    public RejectReason? Classify(long targetTick)
    {
        if (targetTick < _currentTick)
        {
            return RejectReason.Late;
        }

        if (targetTick > _currentTick + SyncProtocol.InputWindowTicks)
        {
            return RejectReason.Future;
        }

        return null;
    }
}
