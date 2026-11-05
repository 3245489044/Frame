namespace Modules.Threading;

/// <summary>
/// 终止理由 —— 回答「为什么停」（why）。与决策值
/// <c>Share.TaskStatusPool.Loops.LoopDecision</c>（回答「以什么终态停」，what）正交。
/// 权威分层：探索终止（<see cref="Cancelled"/>/<see cref="HostClosed"/>）不可落预算裁决；
/// 预算终止（<see cref="BudgetExhausted"/>/<see cref="IdleHung"/>/<see cref="Failed"/>/<see cref="Stagnant"/>）
/// 才映射到 TS/TO/TM。对齐 clinerules AX.TerminationBipartition。
/// </summary>
public enum TerminationReason
{
    /// <summary>未终止（继续推进）。</summary>
    None = 0,

    /// <summary>自然完成（成功）。</summary>
    Completed,

    /// <summary>失败（业务/执行失败，含 fail-fast 熔断）。</summary>
    Failed,

    /// <summary>预算耗尽（总时长到点）。</summary>
    BudgetExhausted,

    /// <summary>空闲挂起（无真实推进心跳超过 idle 窗口）。</summary>
    IdleHung,

    /// <summary>空转（macro 判据连续无推进，达到上限）。</summary>
    Stagnant,

    /// <summary>外部取消（取消令牌，探索终止）。</summary>
    Cancelled,

    /// <summary>宿主关闭（关窗/进程退出，探索终止）。</summary>
    HostClosed,
}
