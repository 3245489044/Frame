using System;

namespace Share.TaskStatusPool.Routing;

/// <summary>
/// 任务路由核心 —— 结合【任务自主标记】与【后台规则驱动】明确区分"什么情况需接入 LLM，什么情况零 LLM"。
/// 路由优先级（按代码逻辑转换，纯函数、可测）：
/// <list type="number">
///   <item>后台规则驱动优先：<see cref="IRuleBackend{TItem}.TryResolve"/> 命中规则 → <see cref="ExecutionDisposition.RuleDriven"/>（零 LLM，规则给出确定性动作）。</item>
///   <item>否则按任务自主标记回退：声明 <see cref="ITaskAutonomy.RequiresLlm"/> → <see cref="ExecutionDisposition.LlmDriven"/>；否则 → <see cref="ExecutionDisposition.RuleDriven"/>（纯确定性，无规则）。</item>
/// </list>
/// 设计：本核心只做判定（纯函数），不承载 LLM / 规则存储 / 任务池接线（分别由消费方注入实现）。
/// 通用任务路由 canonical（SingleImplementation / TH-12）；Agent 等使用层仅调用。
/// </summary>
public static class TaskRouter
{
    /// <summary>裁决任务执行走向。</summary>
    /// <param name="item">任务。</param>
    /// <param name="autonomy">任务自主标记；null = 默认非自主（零 LLM 优先）。</param>
    /// <param name="ruleBackend">后台规则驱动；null = 无规则（零 LLM 时退化为纯确定性）。</param>
    public static TaskRoutingDecision Route<TItem>(
        TItem item,
        ITaskAutonomy? autonomy = null,
        IRuleBackend<TItem>? ruleBackend = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        // ① 后台规则驱动优先：能确定性处理 → 零 LLM
        if (ruleBackend is not null)
        {
            var rule = ruleBackend.TryResolve(item);
            if (rule is not null)
                return TaskRoutingDecision.RuleDriven(rule, "后台规则命中 → 零 LLM 规则驱动");
        }

        // ② 回退任务自主标记
        bool requiresLlm = autonomy?.RequiresLlm ?? false;
        return requiresLlm
            ? TaskRoutingDecision.LlmDriven("任务自主标记声明需 LLM")
            : TaskRoutingDecision.RuleDriven(null, "无规则命中且非自主 → 零 LLM 纯确定性");
    }
}
