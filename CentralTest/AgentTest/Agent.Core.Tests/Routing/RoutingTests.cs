using Agent.Contract.Scheduling;
using Agent.Core.TaskPool;
using Agent.Operation.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.TaskStatusPool.Routing;

namespace Agent.Core.Tests.Routing
{
    [TestClass]
    public class TaskRouterTests
    {
        [TestMethod]
        public void Route_RuleBackendHit_WinsOverLlmAutonomy()
        {
            var decision = TaskRouter.Route(
                "task",
                autonomy: new Autonomy(requiresLlm: true),
                ruleBackend: new RuleBackend(action: "stop"));

            Assert.AreEqual(ExecutionDisposition.RuleDriven, decision.Disposition);
            Assert.IsNotNull(decision.Rule);
            Assert.AreEqual("stop", decision.Rule!.Action);
        }

        [TestMethod]
        public void Route_RuleBackendHit_NonAutonomous()
        {
            var decision = TaskRouter.Route(
                "task",
                autonomy: new Autonomy(requiresLlm: false),
                ruleBackend: new RuleBackend(action: "retry", target: "standard", ruleId: "r1"));

            Assert.AreEqual(ExecutionDisposition.RuleDriven, decision.Disposition);
            Assert.AreEqual("retry", decision.Rule!.Action);
            Assert.AreEqual("standard", decision.Rule!.Target);
            Assert.AreEqual("r1", decision.Rule!.RuleId);
        }

        [TestMethod]
        public void Route_NoRule_RequiresLlm()
        {
            var decision = TaskRouter.Route(
                "task",
                autonomy: new Autonomy(requiresLlm: true),
                ruleBackend: new RuleBackend(action: null));

            Assert.AreEqual(ExecutionDisposition.LlmDriven, decision.Disposition);
            Assert.IsNull(decision.Rule);
        }

        [TestMethod]
        public void Route_NoRule_NonAutonomous_PureDeterministic()
        {
            var decision = TaskRouter.Route(
                "task",
                autonomy: new Autonomy(requiresLlm: false),
                ruleBackend: new RuleBackend(action: null));

            Assert.AreEqual(ExecutionDisposition.RuleDriven, decision.Disposition);
            Assert.IsNull(decision.Rule); // 无规则命中 → 零 LLM 纯确定性（无动作指令）
        }

        [TestMethod]
        public void Route_SchedulingMode_Hybrid_NoRule_RequiresLlm()
        {
            var decision = TaskRouting.Route("task", SchedulingMode.Hybrid, ruleBackend: null);
            Assert.AreEqual(ExecutionDisposition.LlmDriven, decision.Disposition);
        }

        [TestMethod]
        public void Route_SchedulingMode_Config_NoRule_RuleDriven()
        {
            var decision = TaskRouting.Route("task", SchedulingMode.Config, ruleBackend: null);
            Assert.AreEqual(ExecutionDisposition.RuleDriven, decision.Disposition);
        }

        [TestMethod]
        public void Route_SchedulingMode_Deterministic_NoRule_RuleDriven()
        {
            var decision = TaskRouting.Route("task", SchedulingMode.Deterministic, ruleBackend: null);
            Assert.AreEqual(ExecutionDisposition.RuleDriven, decision.Disposition);
        }

        [TestMethod]
        public void Route_NullAutonomy_NullBackend_DefaultsToRuleDriven()
        {
            var decision = TaskRouter.Route("task", autonomy: null, ruleBackend: null);
            Assert.AreEqual(ExecutionDisposition.RuleDriven, decision.Disposition);
            Assert.IsNull(decision.Rule);
        }

        [TestMethod]
        public void Route_SchedulingMode_Hybrid_RuleHit_StillRuleDriven()
        {
            var decision = TaskRouting.Route(
                "task",
                SchedulingMode.Hybrid,
                ruleBackend: new RuleBackend(action: "continue"));
            Assert.AreEqual(ExecutionDisposition.RuleDriven, decision.Disposition);
            Assert.AreEqual("continue", decision.Rule!.Action);
        }

        private sealed class Autonomy : ITaskAutonomy
        {
            private readonly bool _requiresLlm;
            public Autonomy(bool requiresLlm) => _requiresLlm = requiresLlm;
            public bool RequiresLlm => _requiresLlm;
        }

        private sealed class RuleBackend : IRuleBackend<string>
        {
            private readonly string? _action;
            private readonly string? _target;
            private readonly string? _ruleId;

            public RuleBackend(string? action, string? target = null, string? ruleId = null)
            {
                _action = action;
                _target = target;
                _ruleId = ruleId;
            }

            public RuleResolution? TryResolve(string item)
                => _action is null ? null : new RuleResolution(_action, _target, _ruleId);
        }
    }

    [TestClass]
    public class AgentRuleBackendTests
    {
        [TestMethod]
        public void Hybrid_BackendNull_RoutesToLlm()
        {
            var item = new AgentTaskItem { SchedulingMode = SchedulingMode.Hybrid };
            var backend = new AgentRuleBackend();

            Assert.IsNull(backend.TryResolve(item));
            var decision = TaskRouter.Route(item, SchedulingModeAutonomy.From(item.SchedulingMode), backend);
            Assert.AreEqual(ExecutionDisposition.LlmDriven, decision.Disposition);
        }

        [TestMethod]
        public void Config_BackendRule_RoutesToRuleDriven()
        {
            var item = new AgentTaskItem { SchedulingMode = SchedulingMode.Config, SchedulerRulesKey = "scheduler.rules.default" };
            var backend = new AgentRuleBackend();

            var rule = backend.TryResolve(item);
            Assert.IsNotNull(rule);
            Assert.AreEqual(AgentRuleBackend.DefaultRuleAction, rule!.Action);
            Assert.AreEqual("scheduler.rules.default", rule.Target);

            var decision = TaskRouter.Route(item, SchedulingModeAutonomy.From(item.SchedulingMode), backend);
            Assert.AreEqual(ExecutionDisposition.RuleDriven, decision.Disposition);
        }

        [TestMethod]
        public void Deterministic_BackendRule_RoutesToRuleDriven_ZeroLlm()
        {
            var item = new AgentTaskItem { SchedulingMode = SchedulingMode.Deterministic };
            var backend = new AgentRuleBackend();

            Assert.IsNotNull(backend.TryResolve(item));
            var decision = TaskRouter.Route(item, SchedulingModeAutonomy.From(item.SchedulingMode), backend);
            Assert.AreEqual(ExecutionDisposition.RuleDriven, decision.Disposition);
        }

    }
}
