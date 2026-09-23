namespace Operation.StateBlock;

/// <summary>
/// 基建故障重发决策上下文（宿主策略入参）：池把「发生了什么 + 已第几次」交给宿主，
/// 由宿主按运行期纪律（施工单 §13）裁决 —— 池不自带任何领域知识。
/// </summary>
/// <param name="Command">命令名（ask/plan/resume/history）。</param>
/// <param name="Payload">原始 payload（宿主可读续跑证据键；池不解释）。</param>
/// <param name="InfraAttempt">本次为第几次基建故障（1 起；1 = 该请求首次基建故障）。</param>
/// <param name="RetryLimit">配置的重试上限（<see cref="DaemonPoolOptions.InfraRetryLimit"/>）。</param>
/// <param name="Error">失败归因（AEF 结束阶段错误文本）。</param>
/// <param name="LastStage">worker 最后阶段（诊断）。</param>
/// <param name="StderrTail">worker stderr 尾部（诊断）。</param>
public readonly record struct DaemonInfraRetryContext(
    string Command,
    object? Payload,
    int InfraAttempt,
    int RetryLimit,
    string? Error,
    string? LastStage,
    string? StderrTail);

/// <summary>
/// 基建故障重发决策：<paramref name="Allowed"/> = 允许重发（池据此换 worker 再发同一请求）；
/// <paramref name="IsResume"/> = 该次重发是**携证续跑**（非从头重跑）；<paramref name="Reason"/> = 归因（日志/异常文本）。
/// </summary>
/// <param name="Allowed">是否允许重发。</param>
/// <param name="IsResume">是否携证续跑（true ⇒ 复用同一 payload 的续跑键）。</param>
/// <param name="Reason">决策归因（拒绝时即报错归因）。</param>
public readonly record struct DaemonInfraRetryDecision(bool Allowed, bool IsResume, string Reason)
{
    /// <summary>拒绝重发（报错归因）。</summary>
    public static DaemonInfraRetryDecision Reject(string reason) => new(false, false, reason);

    /// <summary>允许重发（首次瞬态豁免 / 携证续跑）。</summary>
    public static DaemonInfraRetryDecision Allow(bool isResume, string reason) => new(true, isResume, reason);
}
