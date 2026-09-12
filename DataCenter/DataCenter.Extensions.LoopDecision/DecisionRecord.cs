using Share.TaskStatusPool.Loops;

namespace Operation.StateBlock.Decision;

/// <summary>
/// 轮决策记录 —— 决策值/词/评估/错误/理由/来源/时间的强类型事实（决策一等化）。
/// 落 StateCenter 可订阅、可审计、可回放；语义域唯一权威在 <see cref="Share.TaskStatusPool.Loops.LoopDecision"/>
/// （R7：StateHoldDriven 只做状态落地，不复制裁决语义），本记录仅承载投影。
/// 决策词经 <see cref="Share.TaskStatusPool.Routing.DecisionWords"/> 归一，禁止魔法键字符串散点（D4 根治）。
/// 对应文档 2026-09-04 StateHoldDriven §3.1 DecisionRecord。
/// </summary>
public sealed record DecisionRecord
{
    /// <summary>裁决语义（TS/TO/TM/Continue/SwitchStrategy/None）。语义权威：Share.TaskStatusPool.Loops.LoopDecision（R7）。</summary>
    public required LoopDecision Decision { get; init; }

    /// <summary>骨架决策词（stop/continue/…，保留供日志/兼容；经 DecisionWords 归一登记）。</summary>
    public string? Word { get; init; }

    /// <summary>本评估结果（pass/partial/fail/parse_error）。</summary>
    public EvalOutcome Eval { get; init; }

    /// <summary>归一错误分类；无错误为 null。</summary>
    public ErrorClass? Error { get; init; }

    /// <summary>终止/决策理由（区分"预算耗尽兜底"与"真超时"，可审计）。</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>决策来源（引擎/骨架/导演/边界）。</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>决策时刻（审计/回放时间戳；UTC —— 跨语言 date-time 契约不绑定本地时区，F-46）。</summary>
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}
