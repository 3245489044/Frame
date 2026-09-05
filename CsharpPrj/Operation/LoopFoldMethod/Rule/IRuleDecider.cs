using System.Text.Json;
using Operation.LoopFoldMethod.Signal;

namespace Operation.LoopFoldMethod.Rule;

/// <summary>
/// 规则决策结果（LLM-free）—— 规则命中 → <see cref="FoldDecision"/> 信号域投影 + 输出元数据（hint/reason/rule 名）。
/// <see cref="ActionWord"/> 为原始决策词（continue/stop/error），供上层直接透传；<see cref="Decision"/> 为其信号域投影
/// （决策信号化：rule 求值 → FoldSignal）。
/// </summary>
public sealed record RuleDecision(
    FoldDecision Decision,
    string ActionWord,
    string? NextPromptHint,
    string? Reason,
    string? RuleName);

/// <summary>
/// 规则决策器通用接缝（LLM-free、Agent 无关）—— 输入求值输入（结构化 JSON）与步数，产出 <see cref="RuleDecision"/>。
/// 具体规则集/引擎由实现方注入（Agent 侧薄适配器基于 Operation.HierarchyMethod.Rules.RuleEngine 实现，规则集经构造注入）；
/// 框架本体不承载配置来源与日志（对齐"规则框架通用架构在 Operation，Agent 只留薄适配"）。
/// 决策词→<see cref="FoldDecision"/> 的单一映射见 <see cref="FoldDecisionMapper"/>（词表单源 Share.TaskStatusPool.Routing.DecisionWords）。
/// </summary>
public interface IRuleDecider
{
    /// <summary>对上一次执行结果（结构化 JSON）按注入规则集求值 → 决策结果。</summary>
    RuleDecision Decide(JsonElement data, int stepCount);

    /// <summary>步数守卫判定（max_steps 保证终止语义由引擎 <c>IsWithinGuard</c> 承担）。</summary>
    bool IsWithinGuard(int stepCount);
}