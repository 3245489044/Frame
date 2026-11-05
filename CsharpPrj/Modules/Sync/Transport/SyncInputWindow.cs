using System;

namespace Modules.Sync.Transport;

/// <summary>输入准入裁决：即“这一条为什么被接受/被丢掉”的可枚举答案。</summary>
public enum SyncInputVerdict
{
    /// <summary>窗口内接受（目标 tick 在当前 tick 或之后窗口内，已进入排序缓冲）。</summary>
    Accepted = 0,

    /// <summary>会话未激活（握手未完成或已被拒绝/关闭），不参与窗口判定。</summary>
    RejectedNotActive = 1,

    /// <summary>太旧：<c>targetTick &lt; currentTick</c>，已滑出窗口。</summary>
    RejectedLate = 2,

    /// <summary>超前瞻：<c>targetTick &gt; currentTick + window</c>，超出排队上限。</summary>
    RejectedFuture = 3,

    /// <summary>同一 <c>(tick, commandId)</c> 已受理过（幂等丢弃）。</summary>
    RejectedDuplicate = 4,

    /// <summary>该 tick 已被消费（已 drain/已应用），迟到的同 tick 输入不再进入。</summary>
    RejectedSlotConsumed = 5,

    /// <summary>同 <c>(tick, 槽位)</c> 已有确定性序更小的键，本次在“首条优先”中落败（不覆盖先到者）。</summary>
    RejectedOutranked = 6,
}

/// <summary>
/// 输入窗口 <c>[current, current + window]</c> 三段语义的唯一实现：
/// <list type="bullet">
/// <item><description>小于 current → <see cref="SyncInputVerdict.RejectedLate"/>（太旧丢弃）</description></item>
/// <item><description>窗口内 → <see cref="SyncInputVerdict.Accepted"/>（未来 tick 也接受，由排序机排队等待该 tick）</description></item>
/// <item><description>大于 current + window → <see cref="SyncInputVerdict.RejectedFuture"/>（超前瞻丢弃）</description></item>
/// </list>
/// <para>窗口宽度是契约常量（<c>InputWindowTicks</c>）：本类只接收构造参数，不写死数值。</para>
/// <para>单线程模型：与 <see cref="SyncServerSession{TRuleSetHash}"/> 同线程独占，不内部加锁。</para>
/// </summary>
public sealed class SyncInputWindow
{
    private long _currentTick;

    /// <param name="windowTicks">窗口宽度（含端点）；契约落地后由 <c>InputWindowTicks</c> 传入。</param>
    /// <param name="initialTick">初始决策 tick（决策 tick 从 0 起）。</param>
    public SyncInputWindow(int windowTicks, long initialTick)
    {
        if (windowTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowTicks), "窗口宽度不能为负。");
        }

        if (initialTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialTick), "决策 tick 不能为负。");
        }

        WindowTicks = windowTicks;
        _currentTick = initialTick;
    }

    /// <summary>窗口宽度（含端点）。</summary>
    public int WindowTicks { get; }

    /// <summary>当前决策 tick。</summary>
    public long CurrentTick => _currentTick;

    /// <summary>窗口上界（含）：<c>current + window</c>。</summary>
    public long UpperBound => _currentTick + WindowTicks;

    /// <summary>推进当前 tick；只允许单调不减，回退视为编程错误而非协议事件。</summary>
    public void AdvanceTo(long tick)
    {
        if (tick < _currentTick)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), "当前 tick 只能单调推进。");
        }

        _currentTick = tick;
    }

    /// <summary>判定一条输入的目标 tick 落在哪一段。</summary>
    public SyncInputVerdict Classify(long targetTick)
    {
        if (targetTick < _currentTick)
        {
            return SyncInputVerdict.RejectedLate;
        }

        if (targetTick > _currentTick + WindowTicks)
        {
            return SyncInputVerdict.RejectedFuture;
        }

        return SyncInputVerdict.Accepted;
    }
}
