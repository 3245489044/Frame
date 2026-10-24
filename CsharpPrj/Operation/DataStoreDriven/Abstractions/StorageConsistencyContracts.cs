using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 一致性事件名与唯一出口（C5：事件名/字段单源；禁各组件自拼诊断 msgId）。
/// <para>Frame 层不含日志框架：接受方经 <see cref="Sink"/> 注入（Agent 侧接 AgentLog，测试侧接断言）。</para>
/// </summary>
public static class StorageConsistencyEvents
{
    /// <summary>SQL 已提交、图侧未应用（已入 Outbox 待重放）。</summary>
    public const string GraphPending = "graph-pending";

    /// <summary>Outbox 条目重放成功。</summary>
    public const string GraphReplayed = "graph-replayed";

    /// <summary>Outbox 条目无法重放（缺重放工厂 / 重放超限）。</summary>
    public const string OutboxAbandoned = "outbox-abandoned";

    /// <summary>对账完成（含差异计数）。</summary>
    public const string ReconcilePass = "reconcile-pass";

    // 归属边准入（S2-4：值沿用 agent_log_messages.json 既有名册键；改名须同步名册）。
    /// <summary>归属边类型未在声明面登记（拒写已发生，仅可见化）。</summary>
    public const string EdgeUndeclared = "ModuleLogReferenceEdgeDiagnostics_EdgeUndeclared";

    /// <summary>边声明面为空 ⇒ 全部边类型拒写。</summary>
    public const string CatalogEmpty = "ModuleLogReferenceEdgeDiagnostics_CatalogEmpty";

    /// <summary>相邻端点键越出当前工作流前缀。</summary>
    public const string NodeKeyOutOfScope = "ModuleLogReferenceEdgeDiagnostics_NodeKeyOutOfScope";

    /// <summary>一致性守护入口失败（对账/重放异常；不阻断存储主路径）。</summary>
    public const string GuardFailed = "storage-guard-failed";

    /// <summary>事件出口（缺省静默；装配期由组合根注入）。</summary>
    public static Action<string, object?>? Sink { get; set; }

    /// <summary>报告一条一致性事件（出口未装配 ⇒ 不抛出、不造假）。</summary>
    public static void Report(string eventName, object? payload)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return;
        try { Sink?.Invoke(eventName, payload); }
        catch { /* 可见化失败不得阻断存储主路径 */ }
    }
}

/// <summary>
/// 一致性守护报告（三入口共用载荷；判据载体 = 计数，非叙述）。
/// </summary>
/// <param name="Check">入口名（LightCheck / ReconcileAll / ReplayPending）。</param>
/// <param name="PendingBefore">进入时 Outbox Pending 数。</param>
/// <param name="PendingAfter">退出时 Outbox Pending 数。</param>
/// <param name="Abandoned">Outbox Abandoned 数（应为 0）。</param>
/// <param name="Dangling">图库悬空端点差异数（未做对账的入口恒 0）。</param>
/// <param name="Reconciled">本次收敛节点数（未做对账的入口恒 0）。</param>
/// <param name="Notes">逐项事实（不含语义评价）。</param>
public sealed record ConsistencyReport(
    string Check,
    int PendingBefore,
    int PendingAfter,
    int Abandoned,
    int Dangling,
    int Reconciled,
    IReadOnlyList<string> Notes)
{
    /// <summary>报告生成时间（UTC；审计用，不参与判据）。</summary>
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 存储一致性守护（三入口、一实现）：启动轻检 / 批次全量对账 / 失败重放。
/// <para>判据（§9）：对账连跑两次第二次 <c>Reconciled=0</c>；Outbox 重放后 <c>Pending=0</c> 且 <c>Abandoned=0</c>。</para>
/// </summary>
public interface IStorageConsistencyGuard
{
    /// <summary>启动一次：Outbox 重放 + 量级快照（不扫全表）。</summary>
    Task<ConsistencyReport> LightCheckAsync(CancellationToken ct = default);

    /// <summary>批次后 / 手动：全量对账（复用引用守卫的两级对账，不复写算法）。</summary>
    Task<ConsistencyReport> ReconcileAllAsync(CancellationToken ct = default);

    /// <summary>图写失败后 / 定时：Outbox 逐条重放（幂等）。</summary>
    Task<ConsistencyReport> ReplayPendingAsync(CancellationToken ct = default);
}
