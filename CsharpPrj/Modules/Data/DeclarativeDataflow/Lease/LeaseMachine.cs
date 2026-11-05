using System;

namespace Modules.DeclarativeDataflow.Lease;

/// <summary>
/// 租约状态机（**挂载/确认/回滚的唯一实现**）—— 与载荷无关：只持有"当前租约 + 待确认租约"，
/// 不感知视图模型、DOM、模板或写计划。
/// <para>
/// 原子性（核心不变量）：<see cref="Reject"/> 只丢弃待确认项，**当前租约保持不变**；
/// <see cref="Confirm"/> 只在 <see cref="LeasePhase.Mounted"/> 生效；未确认的挂载不会改变 <see cref="Current"/>。
/// </para>
/// <para>释放策略见 <see cref="Release"/>：<see cref="LeasePolicy.Persistent"/> 下保留当前租约（返回 false = 未释放）。</para>
/// </summary>
/// <typeparam name="T">租约载荷（引用类型）。</typeparam>
public sealed class LeaseMachine<T> where T : class
{
    private readonly SingleSlot<T> _current = new();
    private readonly SingleSlot<T> _pending = new();

    /// <summary>当前相位。</summary>
    public LeasePhase Phase { get; private set; } = LeasePhase.Created;

    /// <summary>当前生效租约（无 ⇒ null）。</summary>
    public T? Current => _current.Value;

    /// <summary>待确认租约（无 ⇒ null）。</summary>
    public T? Pending => _pending.Value;

    /// <summary>是否存在待确认租约。</summary>
    public bool HasPending => _pending.Has;

    /// <summary>最近一次拒绝原因（诊断用；成功确认不清空历史原因的语义明确：确认即清）。</summary>
    public string? LastRejectReason { get; private set; }

    /// <summary>当前相位映射的运行态受控词（单源 = <see cref="LeasePhaseStages"/>）。</summary>
    public string Stage => LeasePhaseStages.Map(Phase);

    /// <summary>
    /// 申请挂载：写入待确认租约并进入 <see cref="LeasePhase.Mounted"/>。
    /// 允许在 <see cref="LeasePhase.Open"/>（切换）或 <see cref="LeasePhase.Mounted"/>（重试）上覆盖待确认项。
    /// </summary>
    /// <returns>true = 已受理；false = 载荷为空或相位不可挂载（<see cref="LeasePhase.Releasing"/>）。</returns>
    public bool Mount(T? payload)
    {
        if (payload is null) return false;
        if (Phase == LeasePhase.Releasing) return false;

        _pending.Set(payload);
        Phase = LeasePhase.Mounting;
        Phase = LeasePhase.Mounted;
        return true;
    }

    /// <summary>确认：待确认项转正为当前租约。</summary>
    /// <returns>true = 已确认；false = 无待确认项或相位不符（不改变任何状态）。</returns>
    public bool Confirm()
    {
        if (Phase != LeasePhase.Mounted || !_pending.Has) return false;

        _current.Set(_pending.Value);
        _pending.Clear();
        Phase = LeasePhase.Open;
        LastRejectReason = null;
        return true;
    }

    /// <summary>拒绝：丢弃待确认项，当前租约不推进（渲染失败/校验失败路径）。</summary>
    /// <returns>true = 已拒绝；false = 无待确认项或相位不符。</returns>
    public bool Reject(string? reason = null)
    {
        if (Phase != LeasePhase.Mounted) return false;

        _pending.Clear();
        Phase = LeasePhase.Rejected;
        LastRejectReason = reason;
        return true;
    }

    /// <summary>释放：仅在 <see cref="LeasePhase.Open"/> 生效；<see cref="LeasePolicy.Persistent"/> 下不释放。</summary>
    /// <returns>true = 已释放；false = 未释放（相位不符或常驻策略）。</returns>
    public bool Release(LeasePolicy policy = LeasePolicy.OnDemand)
    {
        if (Phase != LeasePhase.Open) return false;
        if (policy == LeasePolicy.Persistent) return false;

        Phase = LeasePhase.Releasing;
        _current.Clear();
        _pending.Clear();
        Phase = LeasePhase.Released;
        return true;
    }

    /// <summary>回到初始相位（测试/宿主重建装配；不承担业务语义）。</summary>
    public void Reset()
    {
        _current.Clear();
        _pending.Clear();
        LastRejectReason = null;
        Phase = LeasePhase.Created;
    }
}
