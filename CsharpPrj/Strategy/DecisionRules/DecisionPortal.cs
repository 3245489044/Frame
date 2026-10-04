using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Strategy.DecisionRules
{
    /// <summary>决策点规则集供给端口（点 → 规则集）。实现由装配层（Agent 等）按各决策点注册；领域中立。</summary>
    public interface IDecisionRuleSetProvider
    {
        /// <summary>取指定决策点规则集；未注册/空返回 null（门面按未命中兜底 stay/continue，fail-safe）。</summary>
        DecisionRuleSet? Get(string decisionPoint);
    }

    /// <summary>
    /// 决策门面（全决策点唯一裁决入口，域中立、LLM-free）：标准调用形态
    /// <code>DecisionPortal.Evaluate(ruleSetOf(point), BuildInput(state)) → DecisionOutput</code>。
    /// 输入装配与输出适配留给薄包装层；本门面只做「取规则集 → DecisionRulesEngine 求值 → 包 DecisionOutput」三件事。
    /// 无命中 / 规则集未注册 / 解析失败 → matched=false, fallback=true（stay/continue fail-safe，不静默吞）。
    /// </summary>
    public sealed class DecisionPortal
    {
        private readonly IDecisionRuleSetProvider _provider;
        private readonly DecisionRulesEngine _engine;

        public DecisionPortal(IDecisionRuleSetProvider provider, DecisionRulesEngine? engine = null)
        {
            _provider = PortGuard.Required(provider, nameof(provider));
            _engine = engine ?? new DecisionRulesEngine(
                new DefaultMatchStrategyRegistry(), new DefaultOutputStrategyRegistry(), new PlaceholderTemplateRenderer());
        }

        /// <summary>动态规则源门面构造：无 provider（规则集每次经 rule-set 重载显式传入）。
        /// 供 ur_config 每次热读 / 规则文件等动态规则源决策点（task_scheduler / rule_task / tool_route）直接走本门面裁决，
        /// 不必为 rule-set 重载提供一个本用不上的空 provider 注册表。</summary>
        public DecisionPortal() : this(NullProvider.Instance, engine: null) { }

        /// <summary>用注入的审计 sink 构造（命中/兜底审计落到 <see cref="IRuleAuditSink"/>，如 DecisionRecorder 适配）。</summary>
        public DecisionPortal(IDecisionRuleSetProvider provider, IRuleAuditSink audit)
        {
            _provider = PortGuard.Required(provider, nameof(provider));
            _engine = new DecisionRulesEngine(
                new DefaultMatchStrategyRegistry(), new DefaultOutputStrategyRegistry(), new PlaceholderTemplateRenderer(), audit: audit);
        }

        /// <summary>装配标准决策输入文档：{ decisionPoint, input, context }（JSON 聚拢点输入契约；根即引擎 Root，field 可用 $.input.* / $.context.*）。</summary>
        public static JsonObject BuildInput(string decisionPoint, JsonObject? input = null, JsonObject? context = null)
            => new()
            {
                ["decisionPoint"] = decisionPoint,
                ["input"] = input ?? new JsonObject(),
                ["context"] = context ?? new JsonObject(),
            };

        /// <summary>求值唯一入口（静态注册供给）：按点取规则集 → 引擎求值 → 包成 <see cref="DecisionOutput"/>。
        /// 委托 <see cref="Evaluate(DecisionRuleSet?,string,JsonObject?,IReadOnlyCollection{string}?)"/>，保证单一引擎裁决路径。</summary>
        public DecisionOutput Evaluate(
            string decisionPoint,
            JsonObject? input,
            IReadOnlyCollection<string>? excludeRuleIds = null)
            => Evaluate(_provider.Get(decisionPoint), decisionPoint, input, excludeRuleIds);

        /// <summary>求值唯一入口（动态规则源，推荐）：显式传入本次规则集 → 引擎求值 → 包成 <see cref="DecisionOutput"/>。
        /// 供规则源随调用变化的决策点（如 ur_config 每次热读的 task_scheduler/rule_task、规则文件 tool_route）直接走门面，
        /// 使全决策点裁决收敛到本门面这一单一外观；<paramref name="decisionPoint"/> 为 JSON 聚拢点键
        /// （BuildInput 的 decisionPoint 一致性身份；规则集身份另由 <paramref name="ruleSet"/>.Id 承载）。</summary>
        public DecisionOutput Evaluate(
            DecisionRuleSet? ruleSet,
            string decisionPoint,
            JsonObject? input,
            IReadOnlyCollection<string>? excludeRuleIds = null)
        {
            var result = _engine.Evaluate(ruleSet, input, excludeRuleIds);
            return DecisionOutput.FromResult(result);
        }

        /// <summary>空 provider（rule-set 重载用不到供给端口；默认构造的占位，避免 _provider 为 null）。</summary>
        private sealed class NullProvider : IDecisionRuleSetProvider
        {
            public static readonly NullProvider Instance = new();
            public DecisionRuleSet? Get(string decisionPoint) => null;
        }
    }
}
