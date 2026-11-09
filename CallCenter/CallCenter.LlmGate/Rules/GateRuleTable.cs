using CallCenter.LlmGate.Model;

namespace CallCenter.LlmGate.Rules;

/// <summary>
/// 默认 Allow、首条命中即裁决的规则表（§6.1）。判据只读结构事实。
/// </summary>
public sealed class GateRuleTable
{
    private readonly GateOptions _options;

    public GateRuleTable(GateOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public GateRuleResult Evaluate(GateContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // 1. kill switch
        if (ctx.Halted)
        {
            return GateRuleResult.Deny(RuleIds.Halt, ReasonCodes.Halted,
                "LlmGate 已 Halt：全局拦截生效（可在窗口 Resume 恢复）");
        }

        // 2. 递归跳数
        if (ctx.Hop >= _options.MaxHop)
        {
            return GateRuleResult.Deny(RuleIds.Hop, ReasonCodes.DenyHop,
                $"递归跳数超限：hop={ctx.Hop} ≥ {_options.MaxHop}");
        }

        // 3. 指纹循环
        if (ctx.FingerprintCountInWindow >= _options.LoopRepeat)
        {
            var nth = ctx.FingerprintCountInWindow + 1;
            return GateRuleResult.Deny(RuleIds.Loop, ReasonCodes.DenyLoop,
                $"同一请求指纹在 {_options.LoopWindowSec}s 内第 {nth} 次出现（阈值 {_options.LoopRepeat}）");
        }

        // 4. 速率
        if (ctx.SessionCallsLastMinute >= _options.CallsPerMinute)
        {
            return GateRuleResult.Deny(RuleIds.Rate, ReasonCodes.DenyRate,
                $"单会话每分钟调用数 {ctx.SessionCallsLastMinute} ≥ {_options.CallsPerMinute}");
        }

        // 5-7. 成本预算（仅已知金额求和；未知数单列，不当 0）
        var unknownNote = ctx.UnknownCostCount > 0 ? $"（另有 {ctx.UnknownCostCount} 条未知）" : string.Empty;
        if (ctx.SessionCostUsd is { } session && session > _options.SessionCostUsd)
        {
            return GateRuleResult.Deny(RuleIds.CostSession, ReasonCodes.DenyCost,
                $"会话累计成本 {session:0.######} > {_options.SessionCostUsd:0.######}{unknownNote}");
        }

        if (ctx.TaskCostUsd is { } task && task > _options.TaskCostUsd)
        {
            return GateRuleResult.Deny(RuleIds.CostTask, ReasonCodes.DenyCost,
                $"任务累计成本 {task:0.######} > {_options.TaskCostUsd:0.######}{unknownNote}");
        }

        if (ctx.DailyCostUsd is { } day && day > _options.DailyCostUsd)
        {
            return GateRuleResult.Deny(RuleIds.CostDay, ReasonCodes.DenyCost,
                $"当日累计成本 {day:0.######} > {_options.DailyCostUsd:0.######}{unknownNote}");
        }

        // 8. token 预算
        if (ctx.DailyTokens is { } tokens && tokens > _options.DailyTokens)
        {
            return GateRuleResult.Deny(RuleIds.Tokens, ReasonCodes.DenyTokens,
                $"当日 token 总量 {tokens:N0} > {_options.DailyTokens:N0}");
        }

        // 9. 人工闸：预估成本达阈值 ∨ 模型无定价
        if (!ctx.PriceKnown && _options.HoldUnknownModel)
        {
            return GateRuleResult.Hold(RuleIds.Manual, ReasonCodes.HoldUnknownModel,
                "模型无定价（成本未知），转人工放行");
        }

        if (ctx.EstimatedCostUsd is { } estimate && estimate >= _options.HoldCostUsd)
        {
            return GateRuleResult.Hold(RuleIds.Manual, ReasonCodes.HoldCost,
                $"预估成本 ≈ {estimate:0.######} ≥ {_options.HoldCostUsd:0.######}，转人工放行");
        }

        // 10. 默认放行
        return GateRuleResult.Allow();
    }
}
