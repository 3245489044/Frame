namespace Operation.LoopFoldMethod.Contracts;

/// <summary>
/// 一次作用点调用 —— 某级作用点收到「当前状态 + 该级阶段」即一次路由/分发输入。
/// 中性契约（不携带任何 Agent/SK 概念，LLM-free）。TState 为宿主状态类型（任意快照）。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <param name="State">当前状态（作用点执行输入）。</param>
/// <param name="Stage">本作用点阶段标识（固定枚举）。</param>
public sealed record ActionCall<TState>(TState State, ActionStageId Stage);

/// <summary>
/// 作用点执行产物 —— handler 处理后返回的新状态。管道沿固定顺序把上一级产物传入下一级，做状态带推进。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <param name="State">处理后的状态（推进给下一级）。</param>
public sealed record HandlerOutcome<TState>(TState State);
