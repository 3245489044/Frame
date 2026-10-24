#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace UiBackend.Html;

/// <summary>
/// 写回指标（B5-2：单源只读埋点；禁第二套记分牌）。
/// <para>口径：<see cref="RenderCount"/> = 整文档渲染次数；<see cref="ApplyLatencyP50Ms"/>/<see cref="ApplyLatencyP95Ms"/> =
/// 计划落地耗时分位（最近 <see cref="LatencyWindow"/> 个样本）；<see cref="UiThreadBlockMs"/> = 写通道累计占用；
/// <see cref="CoalescedOps"/> = 被合并操作数；<see cref="DroppedOps"/> + <see cref="LastDegradeReason"/> = 预算降级可归因。</para>
/// </summary>
public sealed class UiWriteMetrics
{
    private const int LatencyWindow = 64;

    private readonly object _gate = new();
    private readonly long[] _latency = new long[LatencyWindow];
    private int _latencyCount;
    private int _latencyNext;

    private long _renderCount;
    private long _renderBytes;
    private long _plannedOps;
    private long _coalescedOps;
    private long _droppedOps;
    private long _uiThreadBlockMs;
    private long _streamLagMs;
    private long _marshalWaitPeakMs;
    private long _beatNodes;
    private long _beatBytes;
    private long _beatCount;
    private long _sectionsApplied;
    private long _sectionsBulk;
    private long _sectionsSkipped;
    private long _droppedSections;
    private long _queueMsPeak;
    private string? _lastDegradeReason;

    /// <summary>整文档渲染次数（稳态应 ≤ 1 次/分钟）。</summary>
    public long RenderCount => Interlocked.Read(ref _renderCount);

    /// <summary>整文档渲染字节累计。</summary>
    public long RenderBytes => Interlocked.Read(ref _renderBytes);

    /// <summary>计划目标数累计（ops 口径）。</summary>
    public long PlannedOps => Interlocked.Read(ref _plannedOps);

    /// <summary>被合并（去重/在飞合并）的操作数。</summary>
    public long CoalescedOps => Interlocked.Read(ref _coalescedOps);

    /// <summary>被预算丢弃的操作数。</summary>
    public long DroppedOps => Interlocked.Read(ref _droppedOps);

    /// <summary>写通道占用 UI 线程累计（毫秒）。</summary>
    public long UiThreadBlockMs => Interlocked.Read(ref _uiThreadBlockMs);

    /// <summary>流产生 → DOM 落地的滞后（毫秒，最近一次）。</summary>
    public long StreamLagMs => Interlocked.Read(ref _streamLagMs);

    /// <summary>封送排队峰值（毫秒）：&gt;0 且接近耗时 ⇒ UI 线程被占；≈0 ⇒ 耗时在 V8/DOM 执行面。</summary>
    public long MarshalWaitPeakMs => Interlocked.Read(ref _marshalWaitPeakMs);

    /// <summary>最近一次降级原因（<see cref="DroppedOps"/>&gt;0 时必非空）。</summary>
    public string? LastDegradeReason { get { lock (_gate) return _lastDegradeReason; } }

    /// <summary>累计写入节点数（主预算口径）。</summary>
    public long BeatNodes => Interlocked.Read(ref _beatNodes);

    /// <summary>累计写入字节数。</summary>
    public long BeatBytes => Interlocked.Read(ref _beatBytes);

    /// <summary>批次数（一拍一 batch）。</summary>
    public long BeatCount => Interlocked.Read(ref _beatCount);

    /// <summary>已落地节数。</summary>
    public long SectionsApplied => Interlocked.Read(ref _sectionsApplied);

    /// <summary>走 Bulk 原语的节数。</summary>
    public long SectionsBulk => Interlocked.Read(ref _sectionsBulk);

    /// <summary>未变跳过节数。</summary>
    public long SectionsSkipped => Interlocked.Read(ref _sectionsSkipped);

    /// <summary>被预算丢弃的节数（stream 可丢）。</summary>
    public long DroppedSections => Interlocked.Read(ref _droppedSections);

    /// <summary>封送排队峰值（毫秒；按拍窗口）。</summary>
    public long QueueMsPeak => Interlocked.Read(ref _queueMsPeak);

    /// <summary>每拍记账（节点/字节为主预算口径；dropped 必须可归因）。</summary>
    public void OnBeat(int nodes, int bytes, int droppedSections, string? degradeReason = null)
    {
        Interlocked.Increment(ref _beatCount);
        Interlocked.Add(ref _beatNodes, Math.Max(0, nodes));
        Interlocked.Add(ref _beatBytes, Math.Max(0, bytes));
        if (droppedSections > 0) Interlocked.Add(ref _droppedSections, droppedSections);
        if (!string.IsNullOrEmpty(degradeReason)) lock (_gate) _lastDegradeReason = degradeReason;
    }

    /// <summary>单节记账（Bulk 计数用于 I9 复算）。</summary>
    public void OnSection(SectionWriteMode mode, bool applied)
    {
        if (!applied)
        {
            Interlocked.Increment(ref _sectionsSkipped);
            return;
        }

        Interlocked.Increment(ref _sectionsApplied);
        if (mode == SectionWriteMode.BulkReplace) Interlocked.Increment(ref _sectionsBulk);
    }

    /// <summary>封送排队样本（按拍窗口，供 queueMs 分账复算）。</summary>
    public void OnQueueWait(long queueMs)
    {
        var sample = Math.Max(0, queueMs);
        Interlocked.Exchange(ref _queueMsPeak, sample);
        OnMarshalWait(sample);
    }

    /// <summary>整页渲染记账（“整页可被 Replace(shell) 替代”判据的分子）。</summary>
    public void OnRender(int bytes)
    {
        Interlocked.Increment(ref _renderCount);
        Interlocked.Add(ref _renderBytes, Math.Max(0, bytes));
    }

    /// <summary>计划记账（ops + 字节）。</summary>
    public void OnPlan(int ops)
    {
        Interlocked.Add(ref _plannedOps, Math.Max(0, ops));
    }

    /// <summary>合并记账（去重命中/在飞合并）。</summary>
    public void OnCoalesced(int count = 1) => Interlocked.Add(ref _coalescedOps, Math.Max(0, count));

    /// <summary>预算降级记账（每次降级必须可归因）。</summary>
    public void OnDropped(string reason, int count = 1)
    {
        lock (_gate) _lastDegradeReason = reason;
        Interlocked.Add(ref _droppedOps, Math.Max(0, count));
    }

    /// <summary>降级记账（无丢弃但需归因：如超时/慢刷新）。</summary>
    public void OnDegrade(string reason)
    {
        lock (_gate) _lastDegradeReason = reason;
    }

    /// <summary>落地耗时样本（毫秒）：写入滑窗并累计 UI 占用。</summary>
    public void OnApplied(long elapsedMs)
    {
        var sample = Math.Max(0, elapsedMs);
        Interlocked.Add(ref _uiThreadBlockMs, sample);
        lock (_gate)
        {
            _latency[_latencyNext] = sample;
            _latencyNext = (_latencyNext + 1) % LatencyWindow;
            if (_latencyCount < LatencyWindow) _latencyCount++;
        }
    }

    /// <summary>流滞后样本（毫秒）。</summary>
    public void OnStreamLag(long lagMs) => Interlocked.Exchange(ref _streamLagMs, Math.Max(0, lagMs));

    /// <summary>封送排队样本（毫秒）：保留峰值（分位会被偶发样本抹平）。</summary>
    public void OnMarshalWait(long waitMs)
    {
        var sample = Math.Max(0, waitMs);
        long current;
        while (sample > (current = Interlocked.Read(ref _marshalWaitPeakMs)))
        {
            if (Interlocked.CompareExchange(ref _marshalWaitPeakMs, sample, current) == current) break;
        }
    }

    /// <summary>延迟分位（无样本 ⇒ 0）。</summary>
    public double ApplyLatencyPercentile(int percentile)
    {
        int count;
        long[] copy;
        lock (_gate)
        {
            count = _latencyCount;
            copy = _latency.Take(count).ToArray();
        }

        if (count == 0) return 0;
        Array.Sort(copy);
        var index = (int)Math.Ceiling(percentile / 100d * count) - 1;
        index = Math.Clamp(index, 0, count - 1);
        return copy[index];
    }

    /// <summary>P50 落地延迟（毫秒）。</summary>
    public double ApplyLatencyP50Ms => ApplyLatencyPercentile(50);

    /// <summary>P95 落地延迟（毫秒）。</summary>
    public double ApplyLatencyP95Ms => ApplyLatencyPercentile(95);

    /// <summary>单行快照（日志门面消费；字段名稳定供 grep 复算）。</summary>
    public string Snapshot()
        => $"renderCount={RenderCount}, renderBytes={RenderBytes}, plannedOps={PlannedOps}, " +
           $"applyP50={ApplyLatencyP50Ms:F1}ms, applyP95={ApplyLatencyP95Ms:F1}ms, uiThreadBlockMs={UiThreadBlockMs}, " +
           $"coalescedOps={CoalescedOps}, droppedOps={DroppedOps}, streamLagMs={StreamLagMs}, " +
           $"marshalWaitPeakMs={MarshalWaitPeakMs}, degradeReason={(LastDegradeReason ?? "-")}, " +
           $"beatCount={BeatCount}, batchNodes={BeatNodes}, batchBytes={BeatBytes}, " +
           $"sectionsApplied={SectionsApplied}, sectionsBulk={SectionsBulk}, sectionsSkipped={SectionsSkipped}, " +
           $"droppedSections={DroppedSections}, queueMsPeak={QueueMsPeak}";
}
