namespace Operation.LoopFoldMethod.Node.Session;

/// <summary>
/// 装配作用域 —— 标识"本次装配给谁"，消费方据此挂不同的语义源组/命名约定。
/// 引擎只作元数据透传与溯源，不在内部按作用域分支（子智能体 / task / history 走同一条装配路径，TH-12）。
/// </summary>
public enum AssemblyScope
{
    /// <summary>子智能体一步 —— 某执行端（answer / cline / 等）单步提示装配。</summary>
    SubAgentStep,

    /// <summary>任务级 —— task 主循环一轮 / 任务上下文提示装配。</summary>
    TaskRound,

    /// <summary>历史级 —— history 上下文 / 规范消息装配。</summary>
    HistoryRound,
}
