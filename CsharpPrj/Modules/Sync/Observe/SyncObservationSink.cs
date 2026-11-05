namespace Modules.Sync.Observe;

/// <summary>
/// 精确样本出口（sweep 专用）。
/// <para>生产 <c>MetricHistogram</c> 只存 log2 桶计数、分位是桶上界，<b>不得</b>当作拟合样本
/// （观测设计 §2.3 / FG02）。因此同一埋点除写 Metrics 外，可另喂一份<b>精确</b>值给本接口；
/// 由 sweep 侧自己落盘（固定 seed、每档 N 次），拟合只读 sweep 产物。</para>
/// <para>未注入（<c>null</c>）时适配层不产生任何额外分配。</para>
/// </summary>
public interface ISyncObservationSink
{
    /// <param name="point">观测点（17 项之一）。</param>
    /// <param name="value">
    /// 精确值：Histogram 为毫秒、Counter 为本次增量、Gauge 为本次设定值的<b>物理量</b>
    /// （比率已还原为 0..1，不是定点值）。
    /// </param>
    void Sample(SyncObservationPoint point, double value);
}
