namespace Operation.HierarchyMethod.Abstractions;

/// <summary>
/// 簿记流执行结论（ERR-7 / ERR-8 收敛载体）—— 把「未装配 / 失败 / 已提交」从各调用点各自的
/// <c>bool</c> / <c>void</c> / 诊断串 归一为单一结果类型，使调用方**必定**有机会处置（禁静默）。
/// </summary>
/// <param name="Ok">true = 已提交或合法 no-op；false = 未装配执行器或流失败。</param>
/// <param name="Code">诊断码（取 <see cref="DiagnosticsCodes"/>；Ok=true 时为空串）。</param>
/// <param name="Reason">人读归因（Ok=true 时为空串）。</param>
public readonly record struct FlowOutcome(bool Ok, string Code, string Reason)
{
    /// <summary>已提交（或合法 no-op）。</summary>
    public static FlowOutcome Committed() => new(true, string.Empty, string.Empty);

    /// <summary>
    /// 有意不参与（缩减 / 读投影切片）：**已知**本切片不应写该边 / 做该转换 —— 结论 = 已提交（Ok=true），
    /// 与 <see cref="Missing"/>（**真缺失**：未装配，诊断 + 姿态裁决）语义分离，使「有意缩减」不再伪装成「装配缺失」。
    /// </summary>
    /// <param name="reason">人读归因（可选，不产生诊断）。</param>
    public static FlowOutcome NotApplicable(string reason = "") => new(true, string.Empty, reason);

    /// <summary>簿记流入口未装配（调用方须按未完成处置）。</summary>
    /// <param name="reason">人读归因。</param>
    public static FlowOutcome Missing(string reason) => new(false, DiagnosticsCodes.FlowMissing, reason);

    /// <summary>簿记流执行失败。</summary>
    /// <param name="reason">人读归因。</param>
    public static FlowOutcome Failed(string reason) => new(false, DiagnosticsCodes.FlowFailed, reason);
}
