namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 执行进度（心跳进度）：慢调用统一经 <see cref="IProgress{T}"/> 上报推进阶段，
/// 喂给空闲看门狗（区分「慢但正常」与「卡死」），并供观测/UI 展示。
/// 自 Agent.Modules 下沉（LLM 无关的中立契约）。
/// </summary>
public sealed record EndpointProgress(string Stage, string? Message = null);
