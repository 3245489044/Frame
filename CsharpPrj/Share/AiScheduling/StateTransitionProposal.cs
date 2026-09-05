namespace Share.AiScheduling;

/// <summary>
/// 状态导演提议的整体状态转移（自由探索源）。
/// 仅提议、不直接变更状态 —— 校验（<see cref="ITransitionGuard"/>）与应用（状态机）在计算层完成。
/// 所有 Id 以字符串承载（契约纯化，不依赖具体业务枚举/类型）。
/// </summary>
public sealed record StateTransitionProposal(
    SchedulerAction Action,
    string? TargetStrategyId = null,
    string? TargetExecutorId = null,
    string? TargetRunStatusId = null,
    string? Intent = null,
    string? Reason = null,
    double Confidence = 1.0);
