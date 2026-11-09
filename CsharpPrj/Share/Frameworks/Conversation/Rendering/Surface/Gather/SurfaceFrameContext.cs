#nullable enable

using System;
using System.Collections.Generic;
using Share.SurfacePaint.Hosting;

namespace Share.Conversation.Rendering.Surface.Gather;

/// <summary>
/// 帧作用域共享槽（**每窗口一份，非静态**）：桥接同一轮聚拢内跨阶段的产物。
/// <para>为什么需要：producer 签名固定为 <c>(need, ct)</c>，<c>surface-decor</c> 必须同时读
/// <c>surface-plan</c> 的产物与 <c>surface-frame-input</c> 的几何。槽位单写者，写入顺序由声明 <c>deps</c> 保证。</para>
/// <para>纪律：每轮 <see cref="Reset"/> 后重新写入；禁止跨窗口/跨轮复用。</para>
/// </summary>
public sealed class SurfaceFrameContext
{
    private readonly object _gate = new();
    private SurfacePlanBundle _plan = SurfacePlanBundle.Empty;
    private IReadOnlyList<SurfaceCardGeometry> _cards = Array.Empty<SurfaceCardGeometry>();

    /// <summary>本轮计划产物（尚未生产 ⇒ <see cref="SurfacePlanBundle.Empty"/>）。</summary>
    public SurfacePlanBundle Plan
    {
        get { lock (_gate) { return _plan; } }
    }

    /// <summary>本轮采集到的卡片几何（尚未采集 ⇒ 空）。</summary>
    public IReadOnlyList<SurfaceCardGeometry> Cards
    {
        get { lock (_gate) { return _cards; } }
    }

    /// <summary>写入计划产物（单写者 = <c>SurfacePlanGatherModule</c>）。</summary>
    /// <param name="plan">计划产物。</param>
    public void SetPlan(SurfacePlanBundle plan)
    {
        lock (_gate) { _plan = plan ?? SurfacePlanBundle.Empty; }
    }

    /// <summary>写入卡片几何（单写者 = <c>surface-frame-input</c> 生产者）。</summary>
    /// <param name="cards">卡片几何。</param>
    public void SetCards(IReadOnlyList<SurfaceCardGeometry> cards)
    {
        lock (_gate) { _cards = cards ?? Array.Empty<SurfaceCardGeometry>(); }
    }

    /// <summary>每轮开始前清空（禁跨轮残留）。</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _plan = SurfacePlanBundle.Empty;
            _cards = Array.Empty<SurfaceCardGeometry>();
        }
    }
}
