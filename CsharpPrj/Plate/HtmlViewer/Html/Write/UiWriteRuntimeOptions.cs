#nullable enable

namespace UiBackend.Html;

/// <summary>
/// 写回运行时开关与帧预算（B5-1：L3 配置级，运行时可变）。
/// <para>默认值语义：<see cref="FrameBudget"/>=false ⇒ 只记账不丢弃（零回归）；
/// <see cref="InteractiveFastPath"/>=true ⇒ 交互来源不被收敛屏障拖慢（B4 验收所需）；
/// 其余开关默认开启（已是现状契约）。</para>
/// </summary>
public sealed record UiWriteRuntimeOptions
{
    /// <summary>缺省配置（宿主未注入时使用；单一默认源）。</summary>
    public static UiWriteRuntimeOptions Default { get; } = new();

    /// <summary>帧预算开关（关 ⇒ 只记账不丢弃）。</summary>
    public bool FrameBudget { get; init; }

    /// <summary>交互快路径开关（关 ⇒ 交互来源按刷新语义处理）。</summary>
    public bool InteractiveFastPath { get; init; } = true;

    /// <summary>单次刷新计划的操作数上限（预算判据）。</summary>
    public int MaxOpsPerFlush { get; init; } = 256;

    /// <summary>单次刷新计划的字节上限（预算判据）。</summary>
    public int MaxBytesPerFlush { get; init; } = 256 * 1024;

    /// <summary>单拍写入节点数上限（**主预算**，D4 修复；0 = 不限）。</summary>
    public int MaxNodesPerBeat { get; init; } = 1500;

    /// <summary>后台来源（刷新/流）单拍节点数上限（交互不受此限，只受 <see cref="MaxNodesPerBeat"/>）。</summary>
    public int MaxNodesPerBeatBackground { get; init; } = 500;

    /// <summary>节点数超过此值 ⇒ 该节改走 BulkReplace（整体替换，一次 DOM 赋值）。</summary>
    public int BulkNodeThreshold { get; init; } = 800;

    /// <summary>键集合变化率超过此值 ⇒ BulkReplace；1.0 = 永不按比率 Bulk（灰度回退）。</summary>
    public double BulkRatioThreshold { get; init; } = 0.5;

    /// <summary>Bulk 原语总开关（关 ⇒ 恒 Patch，行为等价单原语时代）。</summary>
    public bool BulkReplaceEnabled { get; init; } = true;

    /// <summary>节批量写入口开关（关 ⇒ 旧逐目标 WriteCoreAsync 路径，灰度回退）。</summary>
    public bool UseSectionWriter { get; init; }

    /// <summary>交互车道预留（毫秒/窗口；只记账不阻断，供 queueMs 分账判定）。</summary>
    public int InteractiveReserveMs { get; init; } = 12;

    /// <summary>stream 车道可丢（压力下降级丢弃并计数可见）。</summary>
    public bool StreamDroppable { get; init; } = true;

    /// <summary>F5 渐进表面：按 壳→结构→内容→装饰 排序，预算压力下先丢装饰（关 ⇒ 旧行为）。</summary>
    public bool ProgressiveSurface { get; init; } = true;

    /// <summary>单次刷新的落地耗时上限（毫秒；超限 ⇒ 记账并降级）。</summary>
    public int MaxMsPerFlush { get; init; } = 50;

    /// <summary>指标上报间隔（毫秒；60s 限流，防指标自身洪泛）。</summary>
    public int MetricsReportIntervalMs { get; init; } = 60_000;
}
