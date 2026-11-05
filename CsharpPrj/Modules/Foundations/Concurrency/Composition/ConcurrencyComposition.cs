using System;

namespace Modules.Concurrency;

/// <summary>
/// 并发组合根（唯一）：gate / pipeline / 成本记账 / 组键策略 / 等待语义 只在此装配。
/// <para>调用方禁再 <c>new ConcurrencyBudgetGate(</c>（双门禁 = 同一拆分两次判定）；
/// 组提交只读 pipeline 产出的 <see cref="ConcurrencyAdmission"/> 票据。</para>
/// </summary>
public sealed class ConcurrencyComposition
{
    private ConcurrencyComposition(
        IConcurrencyBudgetGate gate,
        ConcurrencyDecisionPipeline pipeline,
        ConcurrencyCostRecorder? costRecorder,
        IGroupKeyPolicy groupKeys,
        ConcurrencyWaitMode waitMode)
    {
        Gate = gate;
        Pipeline = pipeline;
        CostRecorder = costRecorder;
        GroupKeys = groupKeys;
        WaitMode = waitMode;
    }

    /// <summary>成本门禁（单例）。</summary>
    public IConcurrencyBudgetGate Gate { get; }

    /// <summary>L0 决策管线（Assess→Plan→Validate→Persist→Gate）。</summary>
    public ConcurrencyDecisionPipeline Pipeline { get; }

    /// <summary>组成本记账（未装配成本端口 ⇒ null）。</summary>
    public ConcurrencyCostRecorder? CostRecorder { get; }

    /// <summary>组键派生策略（单源）。</summary>
    public IGroupKeyPolicy GroupKeys { get; }

    /// <summary>组结果交付语义（await | detach）。</summary>
    public ConcurrencyWaitMode WaitMode { get; }

    /// <summary>
    /// 装配唯一组合根。
    /// <paramref name="capacity"/> 延迟解析（池在组合之后构造）；<paramref name="planStore"/> 必填（未落库不得拆分）。
    /// <paramref name="maxMembers"/> = 0 ⇒ 取声明面 <c>split.maxMembers</c>（禁第二处硬编码上限）。
    /// </summary>
    public static ConcurrencyComposition Build(
        ConcurrencyOptions options,
        ICostPriceSource? priceSource,
        Func<IConcurrencyCapacitySource?> capacity,
        ISplitPlanStore planStore,
        IConcurrencyCostSink? costSink = null,
        IConcurrencyUsageQuery? usageQuery = null,
        IGroupKeyPolicy? groupKeys = null,
        ConcurrencyWaitMode waitMode = ConcurrencyWaitMode.Await,
        int maxMembers = 0)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(capacity);
        ArgumentNullException.ThrowIfNull(planStore);

        var memberCap = maxMembers > 0 ? maxMembers : ConcurrencyOptionSpecs.Split.MaxMembers;
        var gate = new ConcurrencyBudgetGate(options, priceSource, capacity);
        var pipeline = new ConcurrencyDecisionPipeline(
            new DeclarativeComplexityAssessor(options),
            new DeclarativeSplitPlanner(options),
            gate,
            planStore,
            memberCap);
        var recorder = costSink is null && usageQuery is null
            ? null
            : new ConcurrencyCostRecorder(options, costSink, usageQuery);

        return new ConcurrencyComposition(
            gate, pipeline, recorder, groupKeys ?? new PlanScopedGroupKeyPolicy(), waitMode);
    }
}
