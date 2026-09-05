using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent.Axioms.Scheduling;
using Agent.Contract.Scheduling;
using Agent.State;
using BasicMethod.Functor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using Share.AiScheduling;
using Share.StateManagement;
using TestInfrastructure;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 配置式调度与 LLM 暂停门的定向测试：
    /// ① SchedulerActionParser 宽容解析单一实现；② ConfigStateDirector（ur_config 规则表驱动，零 LLM）命中/fail-safe/越界目标；
    /// ③ AgentLoopFunction.CheckLlmGateAsync —— 暂停门下沉到 LLM 调用点（null = no-op）。
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.Concurrency)]
    public class ConfigSchedulingTests
    {
        private static SchedulerDirectorContext Ctx(int round = 1, string? strategy = "standard")
            => new(
                CurrentStrategyId: strategy,
                AvailableStrategyIds: new List<string> { "standard", "aggressive", "careful" },
                Telemetry: new SnapshotTelemetry(round, 0, 0, 3, false, 0, null, null),
                AgentStatus: new Dictionary<string, string>(),
                RecentTransitions: new List<string>());

        // ---- SchedulerActionParser ----

        [TestMethod]
        [Timeout(15000)]
        public void SchedulerActionParser_TolerantParsing()
        {
            Assert.AreEqual(SchedulerAction.SwitchStrategy, Parse("switch_strategy"));
            Assert.AreEqual(SchedulerAction.SwitchStrategy, Parse("switch-strategy"));
            Assert.AreEqual(SchedulerAction.SwitchStrategy, Parse("switchStrategy"));
            Assert.AreEqual(SchedulerAction.SwitchStrategy, Parse("SwitchStrategy"));
            Assert.AreEqual(SchedulerAction.Continue, Parse("continue"));
            Assert.AreEqual(SchedulerAction.PauseRun, Parse("pause_run"));
            Assert.IsFalse(SchedulerActionParser.TryParse("", out _));
            Assert.IsFalse(SchedulerActionParser.TryParse("no-such-action", out _));
        }

        private static SchedulerAction Parse(string s) => SchedulerActionParser.TryParse(s, out var a) ? a : throw new AssertFailedException($"解析失败: {s}");

        // ---- ConfigStateDirector（ur_config 规则表驱动，零 LLM）----

        [TestMethod]
        [Timeout(15000)]
        public async Task ConfigStateDirector_NoStore_FailSafe()
        {
            var d = new ConfigStateDirector(null);
            Assert.IsNull(await d.DecideAsync(Ctx()));
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task ConfigStateDirector_MissingKey_FailSafe()
        {
            var d = new ConfigStateDirector(new InMemoryConfigStore());
            Assert.IsNull(await d.DecideAsync(Ctx()));
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task ConfigStateDirector_Hit_ReturnsProposal()
        {
            var store = new InMemoryConfigStore();
            await store.SetAsync(ConfigStateDirector.DefaultRulesKey,
                "{\"rules\":[{\"action\":\"switch_strategy\",\"target_strategy\":\"aggressive\",\"round\":1,\"reason\":\"配置式\"}]}");
            var d = new ConfigStateDirector(store);

            var p = await d.DecideAsync(Ctx(round: 1));

            Assert.IsNotNull(p);
            Assert.AreEqual(SchedulerAction.SwitchStrategy, p.Action);
            Assert.AreEqual("aggressive", p.TargetStrategyId);
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task ConfigStateDirector_TargetOutOfAvailableSet_FailSafe()
        {
            var store = new InMemoryConfigStore();
            await store.SetAsync(ConfigStateDirector.DefaultRulesKey,
                "{\"rules\":[{\"action\":\"switch_strategy\",\"target_strategy\":\"unknown\"}]}");
            var d = new ConfigStateDirector(store);

            // SwitchStrategy 目标越出可用集 → 防幻觉剔除 → null（fail-safe stay）。
            Assert.IsNull(await d.DecideAsync(Ctx()));
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task ConfigStateDirector_NoMatchingRule_FailSafe()
        {
            var store = new InMemoryConfigStore();
            await store.SetAsync(ConfigStateDirector.DefaultRulesKey,
                "{\"rules\":[{\"action\":\"continue\",\"round\":99}]}");
            var d = new ConfigStateDirector(store);

            Assert.IsNull(await d.DecideAsync(Ctx(round: 1)));
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task ConfigStateDirector_MalformedJson_FailSafe()
        {
            var store = new InMemoryConfigStore();
            await store.SetAsync(ConfigStateDirector.DefaultRulesKey, "{ not-json ");
            var d = new ConfigStateDirector(store);

            Assert.IsNull(await d.DecideAsync(Ctx()));
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task ConfigStateDirector_CustomRulesKey_Used()
        {
            var store = new InMemoryConfigStore();
            await store.SetAsync("scheduler.rules.my", "{\"rules\":[{\"action\":\"continue\"}]}");
            var d = new ConfigStateDirector(store, "scheduler.rules.my");

            // 自定义键存在 → 命中 continue；缺省键不存在不干扰。
            Assert.AreEqual(SchedulerAction.Continue, (await d.DecideAsync(Ctx()))!.Action);
        }

        // ---- LLM 暂停门（AgentLoopFunction.CheckLlmGateAsync）----
        // Phase 1（缺陷 1.1）：LLM 门不再为进程级静态单例，经 AgentRunCancellation.SetLlmGate 注入 run 作用域，CheckLlmGateAsync 从作用域读取。

        [TestMethod]
        [Timeout(15000)]
        public async Task LlmGate_WhenSet_IsInvokedBeforeLlm()
        {
            var invoked = 0;
            // 在 run 作用域内注入暂停门，再调用 CheckLlmGateAsync → 从当前 RunContext 读取并执行。
            using (RunContext.Enter(new RunContext(default)))
            {
                RunContext.Current!.LlmGate = _ => { invoked++; return Task.CompletedTask; };

                await AgentLoopFunction.CheckLlmGateAsync(CancellationToken.None);

                Assert.AreEqual(1, invoked, "暂停门应恰好在 LLM 调用前执行一次");
            }
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task LlmGate_Null_IsNoOp()
        {
            // 未注入（无 RunContext / RunContext 内未设门）→ no-op，不应抛异常。
            using (RunContext.Enter(new RunContext(default)))
            {
                RunContext.Current!.LlmGate = null;

                await AgentLoopFunction.CheckLlmGateAsync(CancellationToken.None);
            }

            await AgentLoopFunction.CheckLlmGateAsync(CancellationToken.None); // 无作用域也应 no-op
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task LlmGate_ScopeIsolation_PerRunNotGlobal()
        {
            // 验证缺陷 1.1 修复：门按 run 作用域（RunContext）隔离，不再跨 run 共享进程级全局。
            var invokedOuter = 0;
            var invokedInner = 0;
            using (RunContext.Enter(new RunContext(default)))
            {
                RunContext.Current!.LlmGate = _ => { invokedOuter++; return Task.CompletedTask; };
                await AgentLoopFunction.CheckLlmGateAsync(CancellationToken.None);
                Assert.AreEqual(1, invokedOuter);
            }
            using (RunContext.Enter(new RunContext(default)))
            {
                // 新 RunContext 未注入 → 不应调用 run-a 的门
                await AgentLoopFunction.CheckLlmGateAsync(CancellationToken.None);
                Assert.AreEqual(0, invokedInner);
            }
        }
    }
}
