using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Share.StateManagement;

namespace Share.AiScheduling;

/// <summary>
/// 通用 AI 调度状态机 —— "AI 提议 + 计算守卫 + 原子施加"骨架，状态宿主经 <see cref="IStateFacet"/> 解耦。
/// 每次 <see cref="AdvanceAsync"/> = 导演提议（自由探索）→ 守卫校验（策略完全实施）→ 原子施加（Held/Changed/Reject）。
/// 导演失败 / 置信度不足 / 非法提案 → fail-safe 回退确定性默认动作（stay/continue），不中断调用方。
/// 非限定：无任何 max-iterations / halt / IsCompleted 终止逻辑；继续/终止由外部裁决。
/// 确定性核心不变式：Apply 只改策略/执行状态，不得改写预算/进度/控制位/轮次（违反 → 回退 Reject）。
/// </summary>
/// <typeparam name="TState">整体状态宿主（实现 <see cref="IStateFacet"/>；修改宿主 = 修改状态，不复制平行状态）。</typeparam>
public sealed class AiSchedulerStateMachine<TState> : ISchedulerStateMachine
    where TState : IStateFacet
{
    /// <summary>有界遥测窗口容量（超出时弹出并归档，InformationMonotonicity：只增不丢）。</summary>
    private const int MaxRecentTransitions = 20;

    /// <summary>全量转移历史归档键（窗口弹出时归档到 <see cref="IStateFacet.ReadArchive"/> 区）。</summary>
    private const string HistoryKey = "transitionHistory";

    /// <summary>执行状态缺省（executor 未登记时默认 from 状态，字符串承载）。</summary>
    private const string DefaultRunStatus = "Pending";

    /// <summary>挂起动作缺省目标状态（字符串承载）。</summary>
    private const string PausedRunStatus = "Paused";

    private readonly IStateDirector _director;
    private readonly ITransitionGuard _guard;
    private readonly double _minConfidence;
    private readonly string _arbitrationKey;
    private readonly TState _state;

    /// <param name="director">状态导演（提议源）。</param>
    /// <param name="guard">转移守卫（确定性校验）。</param>
    /// <param name="state">整体状态宿主（实现 <see cref="IStateFacet"/>）。</param>
    /// <param name="minConfidence">最小置信度（低于则 stay，fail-safe）。</param>
    /// <param name="arbitrationKey">仲裁权威锚点（GlobalDecisionKey 实例，默认引擎轴控制流）。</param>
    public AiSchedulerStateMachine(
        IStateDirector director,
        ITransitionGuard guard,
        TState state,
        double minConfidence = 0.0,
        string? arbitrationKey = null)
    {
        _director = director ?? throw new ArgumentNullException(nameof(director));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _minConfidence = minConfidence;
        _arbitrationKey = arbitrationKey ?? "EngineAxis.L1.control-flow";
    }

    /// <summary>混合机制驱动的统一快照（整体状态单一事实源）。</summary>
    public TState State => _state;

    /// <summary>可用策略集（防幻觉目标校验权威来源）。</summary>
    public IReadOnlyList<string> AvailableStrategyIds => _state.AvailableStrategyIds;

    /// <summary>近期转移历史。</summary>
    public IReadOnlyList<string> RecentTransitions => _state.RecentTransitions;

    /// <summary>从统一快照构造导演决策上下文（整体状态只读投影）。</summary>
    /// <param name="telemetryOverride">遥测覆盖（缺省读状态；history 主循环用批次进度合成）。</param>
    /// <param name="historyContext">可选标准上下文精简文本（history 主循环视图的已渲染摘要，供导演参考对话历史）；非 history 调度不传（null）。</param>
    public SchedulerDirectorContext BuildContext(
        SnapshotTelemetry? telemetryOverride = null,
        string? historyContext = null)
    {
        var telemetry = telemetryOverride ?? new SnapshotTelemetry(
            _state.Round, _state.BudgetLevel, _state.TokenValue, _state.TokenMax, _state.Halt,
            _state.ContinuousFailures, null, null, null);
        return new SchedulerDirectorContext(
            string.IsNullOrWhiteSpace(_state.CurrentStrategyId) ? null : _state.CurrentStrategyId,
            new List<string>(_state.AvailableStrategyIds),
            telemetry,
            new Dictionary<string, string>(_state.AgentStatus, StringComparer.Ordinal),
            new List<string>(_state.RecentTransitions),
            historyContext);
    }

    /// <summary>
    /// 推进一次整体状态：导演提议 → 计算守卫校验 → 原子施加（Held/Changed/Reject）。
    /// 导演失败 / 置信度不足 / 非法提案 → fail-safe 回退确定性默认动作（stay/continue），不中断调用方。
    /// </summary>
    public async Task<SchedulerDecision> AdvanceAsync(
        SchedulerDirectorContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = context ?? BuildContext();

        StateTransitionProposal? proposal;
        try
        {
            proposal = await _director.DecideAsync(ctx, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // fail-safe：导演异常 → stay（不中断调用方）。
            return Stay(ctx, null, SchedulerAction.Continue);
        }

        if (proposal is null || proposal.Confidence < _minConfidence)
            return Stay(ctx, proposal, proposal?.Action ?? SchedulerAction.Continue);

        var verdict = _guard.Verify(proposal, ctx);
        if (verdict == TransitionVerdict.Reject)
            return Fallback(ctx, proposal);

        return ApplyGuarded(ctx, proposal, verdict);
    }

    /// <summary>确定性核心不变式守卫：Apply 只能改策略/执行状态，不得改写预算/进度/控制位/轮次。违反 → 回退 Reject。</summary>
    private SchedulerDecision ApplyGuarded(SchedulerDirectorContext ctx, StateTransitionProposal p, TransitionVerdict verdict)
    {
        var b0 = _state.BudgetLevel;
        var t0 = _state.TokenValue;
        var h0 = _state.Halt;
        var r0 = _state.Round;

        var decision = Apply(ctx, p, verdict);

        // 确定性核心不变式（防御性断言）：Apply 不得触碰预算/进度/控制位/轮次。
        // 核心字段经 IStateFacet 只读，Apply 无法经门面改写；一旦被改写说明宿主/守卫实现破坏不变式，且无法经门面回滚，
        // 故显式抛错（而非返回误导性的 Reject——Reject 语义=未施加，此处状态已无法保证）。
        if (_state.BudgetLevel != b0 || _state.TokenValue != t0 || _state.Halt != h0 || _state.Round != r0)
            throw new InvalidOperationException(
                $"Apply violated deterministic-core invariant (budget/token/halt/round mutated): budget {b0}->{_state.BudgetLevel}, token {t0}->{_state.TokenValue}, halt {h0}->{_state.Halt}, round {r0}->{_state.Round}.");
        return decision;
    }

    // ---- 施加（策略完全实施 + 离散状态合法边校验已由守卫完成）----

    private SchedulerDecision Apply(SchedulerDirectorContext ctx, StateTransitionProposal p, TransitionVerdict verdict)
        => p.Action switch
        {
            SchedulerAction.SwitchStrategy => ApplySwitch(ctx, p),
            SchedulerAction.AdvanceRun or SchedulerAction.Retry => ApplyRunStatus(ctx, p, toPaused: false),
            SchedulerAction.PauseRun => ApplyRunStatus(ctx, p, toPaused: true),
            SchedulerAction.Replan or SchedulerAction.Explore => Record(ctx, p, verdict, applied: true, newStatus: null, targetStrategy: null),
            _ => Stay(ctx, p, SchedulerAction.Continue),
        };

    private SchedulerDecision ApplySwitch(SchedulerDirectorContext ctx, StateTransitionProposal p)
    {
        var target = p.TargetStrategyId;
        if (string.IsNullOrWhiteSpace(target))
            return Stay(ctx, p, SchedulerAction.SwitchStrategy);

        var same = string.Equals(_state.CurrentStrategyId, target, StringComparison.Ordinal);
        _state.CurrentStrategyId = target;
        RecordTransition($"strategy:{target} | {p.Reason}");
        return Decide(
            applied: !same, action: SchedulerAction.SwitchStrategy,
            verdict: same ? TransitionVerdict.Held : TransitionVerdict.Pass,
            targetStrategy: target, intent: p.Intent, reason: p.Reason,
            confidence: p.Confidence, arbitrationKey: _arbitrationKey);
    }

    private SchedulerDecision ApplyRunStatus(SchedulerDirectorContext ctx, StateTransitionProposal p, bool toPaused)
    {
        var executor = p.TargetExecutorId ?? ctx.AgentStatus.Keys.FirstOrDefault();
        var target = p.TargetRunStatusId ?? (toPaused ? PausedRunStatus : null);
        if (executor is null || string.IsNullOrWhiteSpace(target))
            return Stay(ctx, p, p.Action);

        var from = ctx.AgentStatus.TryGetValue(executor, out var f) && !string.IsNullOrWhiteSpace(f) ? f : DefaultRunStatus;

        // 与 ApplySwitch 保持一致：same 由目标与当前实际值比较判定，而非信任外部裁决
        // （避免 Held 语义混用导致"已变更却报未施加"）；仅在真正变更时写状态并记录转移。
        var same = string.Equals(from, target, StringComparison.Ordinal);
        if (!same)
        {
            _state.AgentStatus[executor] = target;
            RecordTransition($"{executor}:{from}→{target} | {p.Reason}");
        }

        return Decide(
            applied: !same, action: p.Action,
            verdict: same ? TransitionVerdict.Held : TransitionVerdict.Pass,
            targetRunStatus: target, targetExecutor: executor,
            intent: p.Intent, reason: p.Reason, confidence: p.Confidence, arbitrationKey: _arbitrationKey);
    }

    private SchedulerDecision Record(SchedulerDirectorContext ctx, StateTransitionProposal p, TransitionVerdict verdict, bool applied, string? newStatus, string? targetStrategy)
    {
        RecordTransition($"{p.Action}:{p.Intent ?? p.Reason}");
        return Decide(
            applied: applied, action: p.Action, verdict: verdict,
            targetStrategy: targetStrategy, targetRunStatus: newStatus,
            intent: p.Intent, reason: p.Reason, confidence: p.Confidence, arbitrationKey: _arbitrationKey);
    }

    private SchedulerDecision Fallback(SchedulerDirectorContext ctx, StateTransitionProposal p)
    {
        // 非法/越界提案：回退确定性默认动作（continue），不施加，记录为 Reject。
        RecordTransition($"reject:{p.Action} | {p.Reason}");
        return Decide(
            applied: false, action: p.Action, verdict: TransitionVerdict.Reject,
            targetStrategy: p.TargetStrategyId, targetRunStatus: p.TargetRunStatusId,
            targetExecutor: p.TargetExecutorId, intent: p.Intent, reason: p.Reason,
            confidence: p.Confidence, arbitrationKey: _arbitrationKey);
    }

    private SchedulerDecision Stay(SchedulerDirectorContext ctx, StateTransitionProposal? p, SchedulerAction action)
        => Decide(
            applied: false, action: action, verdict: TransitionVerdict.Held,
            targetStrategy: p?.TargetStrategyId, targetRunStatus: p?.TargetRunStatusId,
            targetExecutor: p?.TargetExecutorId, intent: p?.Intent, reason: p?.Reason,
            confidence: p?.Confidence ?? 0.0, arbitrationKey: _arbitrationKey);

    /// <summary>
    /// <see cref="SchedulerDecision"/> 构造单一收敛点（D3）：统一填充 10+ 命名参数与仲裁锚点，
    /// 消除 ApplySwitch/ApplyRunStatus/Record/Fallback/Stay 五处的同构重复构造。
    /// </summary>
    private static SchedulerDecision Decide(
        bool applied, SchedulerAction action, TransitionVerdict verdict,
        string? targetStrategy = null, string? targetRunStatus = null, string? targetExecutor = null,
        string? intent = null, string? reason = null, double confidence = 0.0, string? arbitrationKey = null)
        => new(applied, action, verdict, targetStrategy, targetRunStatus, targetExecutor, intent, reason, confidence, arbitrationKey);

    private void RecordTransition(string text)
    {
        _state.RecentTransitions.Add(text);
        if (_state.RecentTransitions.Count > MaxRecentTransitions)
        {
            var evicted = _state.RecentTransitions[0];
            _state.RecentTransitions.RemoveAt(0);
            // 全量只增：弹出有界遥测窗口时归档到归档区（InformationMonotonicity：信息不丢失，可跨会话持久化）。
            var cur = _state.ReadArchive(HistoryKey);
            _state.WriteArchive(HistoryKey, string.IsNullOrEmpty(cur) ? evicted : cur + "\n" + evicted);
        }
    }
}
