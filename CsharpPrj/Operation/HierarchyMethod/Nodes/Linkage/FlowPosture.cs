using System;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Abstractions;
using Share.Foundations.Diagnostics;

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
/// 默认姿态：各调用点缺省取 <see cref="RequireFlow"/>（写入口缺失即可见化，禁静默回退旧直写）；
/// 组合根可在装配期显式改选 <see cref="RequireFlow"/> 完成收口。
/// </para>
/// </summary>
public sealed class FlowPosture
{
    /// <summary>迁移期回退姿态已删除（RED-2）：写入口缺失一律可见化，禁静默回退旧直写。</summary>
    public static readonly FlowPosture RequirePort = new(FlowPostureKind.RequirePort);

    /// <summary>收口姿态：写入口缺失 ⇒ <see cref="FlowOutcome.Missing"/>（可见化，禁直调）。</summary>
    public static readonly FlowPosture RequireFlow = RequirePort;

    /// <summary>
    /// 缩减切片姿态：端口缺失 = **有意不参与**（合法、预期）⇒ 静默返回 <see cref="FlowOutcome.NotApplicable"/>，
    /// **不发诊断**。治「缩减切片每步误报 <c>flow_entry_missing</c>」—— 与真缺失（<see cref="RequireFlow"/>
    /// <see cref="RequireFlow"/>）在装配期一处选定区分。
    /// </summary>
    public static readonly FlowPosture Reduced = new(FlowPostureKind.Reduced);

    /// <param name="kind">姿态种类（装配期一处选定）。</param>
    public FlowPosture(FlowPostureKind kind) => Kind = kind;

    /// <summary>姿态种类。</summary>
    public FlowPostureKind Kind { get; }

    /// <summary>是否缩减切片姿态（端口缺失 = 有意不参与，静默不报）。</summary>
    public bool IsReduced => Kind == FlowPostureKind.Reduced;

    /// <summary>
    /// 驱动一次簿记转换：入口缺失时按姿态裁决（缩减 / 拒绝），结果与归因单一回报。
    /// </summary>
    /// <param name="flow">流入口调用（null = 未装配入口）。</param>
    /// <param name="diagnostics">诊断端口（可空 = 不记）。</param>
    /// <param name="context">归因上下文标签（诊断码载荷，供检索 / 门禁对账）。</param>
    /// <param name="payload">附加上下文载荷（可空）。</param>
    public async Task<FlowOutcome> RunAsync(
        Func<Task>? flow,
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

        diagnostics?.Report(DiagnosticsCodes.FlowMissing, Merge(context, payload));
        return FlowOutcome.Missing($"{context}：簿记流入口未装配（姿态={Kind}）");
    }

    /// <summary>
    /// 驱动一次**带结论**的簿记转换（返回 bool 的流形态）：
    /// 流返回 <c>false</c> ⇒ 结论 = <see cref="FlowOutcome.Failed"/>（调用方据此可见化 / 上报，禁静默）。
    /// </summary>
    /// <param name="flow">流入口调用（null = 未装配入口）。</param>
    /// <param name="diagnostics">诊断端口（可空 = 不记）。</param>
    /// <param name="context">归因上下文标签。</param>
    /// <param name="payload">附加上下文载荷（可空）。</param>
    public async Task<FlowOutcome> RunAsync(
        Func<Task<bool>>? flow,
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

        diagnostics?.Report(DiagnosticsCodes.FlowMissing, Merge(context, payload));
        return FlowOutcome.Missing($"{context}：簿记流入口未装配（姿态={Kind}）");
    }

    // ===== 端口感知重载（M2 收拢）：把「flow is null ? null : () => flow.XxxAsync(...)」收进姿态，
    //      调用点零判空（不再手写 `_xxxFlow is null`），缺失处置口径单源。=====

    /// <summary>驱动一次轮末沉淀转换（<see cref="IRoundAttributionFlow.SettleRoundAsync"/> 形态）。</summary>
    public Task<FlowOutcome> RunAsync(
        IRoundAttributionFlow? flow, RoundAttributionRequest request,
        IDiagnosticsSink? diagnostics, string context, object? payload = null, CancellationToken ct = default)
        => RunAsync(flow is null ? null : async () => { await flow.SettleRoundAsync(request, ct).ConfigureAwait(false); },
            diagnostics, context, payload);

    /// <summary>驱动一次产出物化转换（<see cref="IProductionsFlow.AcceptAndMaterializeAsync"/> 形态）。</summary>
    public Task<FlowOutcome> RunAsync(
        IProductionsFlow? flow, ProductionsRequest request,
        IDiagnosticsSink? diagnostics, string context, object? payload = null, CancellationToken ct = default)
        => RunAsync(flow is null ? null : async () => { await flow.AcceptAndMaterializeAsync(request, ct).ConfigureAwait(false); },
            diagnostics, context, payload);

    private static object Merge(string context, object? payload)
        => payload is null ? new { context } : new { context, payload };
}

/// <summary>回退姿态种类（装配期由组合根一处选定；旧 AllowLegacyFallback 已删除）。</summary>
public enum FlowPostureKind
{
    /// <summary>收口后：禁直调回退，入口缺失即返回 <see cref="FlowOutcome.Missing"/>。</summary>
    RequirePort = 0,

    /// <summary>缩减切片：端口缺失 = 有意不参与 ⇒ 静默返回 <see cref="FlowOutcome.NotApplicable"/>，不发诊断。</summary>
    Reduced = 1,
}
