using System;

namespace Share.TaskStatusPool.Routing;

/// <summary>
/// 任务执行走向 —— 明确区分"是否需要在任务池中接入 LLM"：
/// <list type="bullet">
///   <item><see cref="RuleDriven"/>：零 LLM，后台规则驱动 / 纯确定性处理。</item>
///   <item><see cref="LlmDriven"/>：需 LLM（自主生成 / 工具），接入 LLM 执行。</item>
/// </list>
/// 由 <see cref="TaskRouter"/> 结合任务自主标记 + 后台规则驱动裁决产出。
/// 通用任务路由 canonical（SingleImplementation / TH-12），消费方（Agent 等使用层）仅调用。
/// </summary>
public enum ExecutionDisposition
{
    /// <summary>零 LLM：后台规则驱动 / 纯确定性处理。</summary>
    RuleDriven,

    /// <summary>需 LLM：接入 LLM 执行（自主生成 / 工具）。</summary>
    LlmDriven,
}

/// <summary>
/// 后台规则驱动的确定性解析结果（零 LLM 路径的"动作指令"）。
/// </summary>
public sealed class RuleResolution
{
    /// <summary>规则命中给出的确定性动作（如 continue / stop / retry / switch_strategy …）。</summary>
    public string Action { get; }

    /// <summary>动作目标（可选；如目标策略 / 执行器 Id）。</summary>
    public string? Target { get; }

    /// <summary>命中规则标识（观测 / 归因；可选）。</summary>
    public string? RuleId { get; }

    public RuleResolution(string action, string? target = null, string? ruleId = null)
    {
        Action = PortGuard.Required(action, nameof(action));
        Target = target;
        RuleId = ruleId;
    }

    public override string ToString()
        => $"Rule(action={Action}{(Target is null ? "" : $", target={Target}")}{(RuleId is null ? "" : $", rule={RuleId}")})";
}

/// <summary>
/// 任务路由裁决 —— 任务自主标记 + 后台规则驱动结合的最终走向（<see cref="TaskRouter"/> 的产出）。
/// </summary>
public readonly record struct TaskRoutingDecision(
    ExecutionDisposition Disposition,
    string? Reason,
    RuleResolution? Rule = null)
{
    public static TaskRoutingDecision RuleDriven(RuleResolution? rule, string? reason = null)
        => new(ExecutionDisposition.RuleDriven, reason ?? "后台规则驱动 / 零 LLM", rule);

    public static TaskRoutingDecision LlmDriven(string? reason = null)
        => new(ExecutionDisposition.LlmDriven, reason ?? "任务自主标记声明需 LLM");
}
