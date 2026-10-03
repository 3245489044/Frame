using System;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Abstractions;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>
/// 迁移期回退姿态（DEBT-1 / DUP-1 收敛）—— 把「流入口是否存在」从**每个调用点的 if/else**
/// 提为**装配期一处选定**的策略对象：调用点只写一次正路（流），回退与否由本对象裁决。
/// <para>
/// 收益：删除回退支 = 翻一个开关（<see cref="RequireFlow"/>，或整体不再传 legacy 委托），
/// 而不是逐文件找 <c>else</c>；且回退一旦被走到即以 <see cref="DiagnosticsCodes.FlowFallback"/>
/// 计数可见化，使「回退是否仍在被使用」成为可门禁的观测事实（E14 基线只减不增）。
/// </para>
/// <para>
/// 零行为变化默认：各调用点缺省取 <see cref="AllowLegacyFallback"/>（与迁移期语义一致）；
/// 组合根可在装配期显式改选 <see cref="RequireFlow"/> 完成收口。
/// </para>
/// </summary>
public sealed class FlowPosture
{
    /// <summary>收口姿态：流入口缺失 ⇒ **不执行回退**，返回 <see cref="FlowOutcome.Missing"/>（可见化，禁直调）。</summary>
    public static readonly FlowPosture RequireFlow = new(FlowPostureKind.RequireFlow);

    /// <summary>迁移期姿态：流入口缺失 ⇒ 允许直调回退（回退计数可见化）；流入口存在时不回退。</summary>
    public static readonly FlowPosture AllowLegacyFallback = new(FlowPostureKind.AllowLegacyFallback);

    /// <summary>
    /// 缩减切片姿态：端口缺失 = **有意不参与**（合法、预期）⇒ 静默返回 <see cref="FlowOutcome.NotApplicable"/>，
    /// **不发诊断**。治「缩减切片每步误报 <c>flow_entry_missing</c>」—— 与真缺失（<see cref="AllowLegacyFallback"/> /
    /// <see cref="RequireFlow"/>）在装配期一处选定区分。
    /// </summary>
    public static readonly FlowPosture Reduced = new(FlowPostureKind.Reduced);

    /// <param name="kind">姿态种类（装配期一处选定）。</param>
    public FlowPosture(FlowPostureKind kind) => Kind = kind;

    /// <summary>姿态种类。</summary>
    public FlowPostureKind Kind { get; }

    /// <summary>是否允许回退直调（仅在流入口缺失时生效）。</summary>
    public bool AllowsFallback => Kind == FlowPostureKind.AllowLegacyFallback;

    /// <summary>是否缩减切片姿态（端口缺失 = 有意不参与，静默不报）。</summary>
    public bool IsReduced => Kind == FlowPostureKind.Reduced;

    /// <summary>
    /// 驱动一次簿记转换：优先流；流入口缺失时按姿态裁决（回退 / 拒绝），结果与归因单一回报。
    /// </summary>
    /// <param name="flow">流入口调用（null = 未装配入口）。</param>
    /// <param name="legacy">回退直调（null = 无可用回退；<paramref name="flow"/> 非 null 时不会被调用）。</param>
    /// <param name="diagnostics">诊断端口（可空 = 不记）。</param>
    /// <param name="context">归因上下文标签（诊断码载荷，供检索 / 门禁对账）。</param>
    /// <param name="payload">附加上下文载荷（可空）。</param>
    public async Task<FlowOutcome> RunAsync(
        Func<Task>? flow,
        Func<Task>? legacy,
        IDiagnosticsSink? diagnostics,
        string context,
        object? payload = null)
    {
        if (flow is not null)
        {
            await flow().ConfigureAwait(false);
            return FlowOutcome.Committed();
        }

        if (IsReduced)
            return FlowOutcome.NotApplicable($"{context}：本切片不参与（姿态={Kind}）");

        if (AllowsFallback && legacy is not null)
        {
            diagnostics?.Report(DiagnosticsCodes.FlowFallback, Merge(context, payload));
            await legacy().ConfigureAwait(false);
            return FlowOutcome.Committed();
        }

        diagnostics?.Report(DiagnosticsCodes.FlowMissing, Merge(context, payload));
        return FlowOutcome.Missing($"{context}：簿记流入口未装配（姿态={Kind}）");
    }

    /// <summary>
    /// 驱动一次**带结论**的簿记转换（<c>ITaskProvisionFlow.ProvisionAsync</c> 形态）：
    /// 流返回 <c>false</c> ⇒ 结论 = <see cref="FlowOutcome.Failed"/>（调用方据此可见化 / 上报，禁静默）。
    /// </summary>
    /// <param name="flow">流入口调用（null = 未装配入口）。</param>
    /// <param name="legacy">回退直调（null = 无可用回退）。</param>
    /// <param name="diagnostics">诊断端口（可空 = 不记）。</param>
    /// <param name="context">归因上下文标签。</param>
    /// <param name="payload">附加上下文载荷（可空）。</param>
    public async Task<FlowOutcome> RunAsync(
        Func<Task<bool>>? flow,
        Func<Task>? legacy,
        IDiagnosticsSink? diagnostics,
        string context,
        object? payload = null)
    {
        if (flow is not null)
        {
            if (await flow().ConfigureAwait(false))
                return FlowOutcome.Committed();

            diagnostics?.Report(DiagnosticsCodes.FlowFailed, Merge(context, payload));
            return FlowOutcome.Failed($"{context}：簿记流返回 false（未装配执行器或流失败）");
        }

        if (IsReduced)
            return FlowOutcome.NotApplicable($"{context}：本切片不参与（姿态={Kind}）");

        if (AllowsFallback && legacy is not null)
        {
            diagnostics?.Report(DiagnosticsCodes.FlowFallback, Merge(context, payload));
            await legacy().ConfigureAwait(false);
            return FlowOutcome.Committed();
        }

        diagnostics?.Report(DiagnosticsCodes.FlowMissing, Merge(context, payload));
        return FlowOutcome.Missing($"{context}：簿记流入口未装配（姿态={Kind}）");
    }

    // ===== 端口感知重载（M2 收拢）：把「flow is null ? null : () => flow.XxxAsync(...)」收进姿态，
    //      调用点零判空（不再手写 `_xxxFlow is null`），缺失处置口径单源。=====

    /// <summary>驱动一次归属边转换（<see cref="INodeAttributionFlow.AttachAsync"/> 形态）。</summary>
    public Task<FlowOutcome> RunAsync(
        INodeAttributionFlow? flow, string taskKey, string workflowId, string nodeKey,
        IDiagnosticsSink? diagnostics, string context, object? payload = null, CancellationToken ct = default)
        => RunAsync(flow is null ? null : () => flow.AttachAsync(taskKey, workflowId, nodeKey, ct),
            legacy: null, diagnostics, context, payload);

    /// <summary>驱动一次任务供给转换（<see cref="ITaskProvisionFlow.ProvisionAsync"/> 形态，带结论）。</summary>
    public Task<FlowOutcome> RunAsync(
        ITaskProvisionFlow? flow, string? historyId, string? historyName, string taskKey,
        IDiagnosticsSink? diagnostics, string context, object? payload = null, CancellationToken ct = default)
        => RunAsync(flow is null ? null : () => flow.ProvisionAsync(historyId, historyName, taskKey, ct),
            legacy: null, diagnostics, context, payload);

    /// <summary>驱动一次轮末沉淀转换（<see cref="IRoundAttributionFlow.SettleRoundAsync"/> 形态）。</summary>
    public Task<FlowOutcome> RunAsync(
        IRoundAttributionFlow? flow, RoundAttributionRequest request,
        IDiagnosticsSink? diagnostics, string context, object? payload = null, CancellationToken ct = default)
        => RunAsync(flow is null ? null : async () => { await flow.SettleRoundAsync(request, ct).ConfigureAwait(false); },
            legacy: null, diagnostics, context, payload);

    /// <summary>驱动一次产出物化转换（<see cref="IProductionsFlow.AcceptAndMaterializeAsync"/> 形态）。</summary>
    public Task<FlowOutcome> RunAsync(
        IProductionsFlow? flow, ProductionsRequest request,
        IDiagnosticsSink? diagnostics, string context, object? payload = null, CancellationToken ct = default)
        => RunAsync(flow is null ? null : async () => { await flow.AcceptAndMaterializeAsync(request, ct).ConfigureAwait(false); },
            legacy: null, diagnostics, context, payload);

    private static object Merge(string context, object? payload)
        => payload is null ? new { context } : new { context, payload };
}

/// <summary>回退姿态种类（装配期由组合根一处选定）。</summary>
public enum FlowPostureKind
{
    /// <summary>迁移期：流入口缺失时允许直调回退（经诊断计数可见化）。</summary>
    AllowLegacyFallback = 0,

    /// <summary>收口后：禁直调回退，流入口缺失即返回 <see cref="FlowOutcome.Missing"/>。</summary>
    RequireFlow = 1,

    /// <summary>缩减切片：端口缺失 = 有意不参与 ⇒ 静默返回 <see cref="FlowOutcome.NotApplicable"/>，不发诊断。</summary>
    Reduced = 2,
}
