namespace Share.AiScheduling;

/// <summary>
/// 一次 AI 调度决策（<see cref="ISchedulerStateMachine.AdvanceAsync"/> 的产出）。
/// 是否施加（Applied）+ 动作 + 校验裁决 + 目标 + 推理（决策可观测，供宿主消费/记录）。
/// <see cref="ArbitrationKey"/> 为仲裁权威锚点（把"谁裁决"落成可观测事实）。
/// </summary>
public sealed record SchedulerDecision(
    bool Applied,
    SchedulerAction Action,
    TransitionVerdict Verdict,
    string? TargetStrategyId = null,
    string? TargetRunStatusId = null,
    string? TargetExecutorId = null,
    string? Intent = null,
    string? Reason = null,
    double Confidence = 0.0,
    string? ArbitrationKey = null,
    // E4：导演<b>未产出提议</b>（降级/异常 → 状态机 fail-safe stay）的真实事实；供回灌 director_state 轴
    // （route=switch 规则依赖它，避免"恒不可达"）。低置信度但确有提议 → false（非降级）。
    bool DirectorDegraded = false);
