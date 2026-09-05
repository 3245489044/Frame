using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Contract.Answer;
using Agent.Contract.Llm;
using Agent.Modules.StatelessAnswer;

namespace Agent.Modules.Tests
{
    /// <summary>
    /// 逐轮提示词计划（<see cref="RoundPromptPlan"/>）测试：验证"完全显式控制调用轮数"的内层控制面——
    /// 每轮 system / user 内容可按轮次显式给定，未命中按优先级回退；缺省 null 完全保持既有单模板行为。
    /// </summary>
    [TestClass]
    [TestCategory("Modules_Unit")]
    public class RoundPromptPlanTests
    {
        private static StatelessAnswerExecutor NewExecutor(RoundPromptPlan? plan = null, string? system = null)
            => new(new FakeLlmClient(), systemPrompt: system ?? "SYS", roundPromptPlan: plan);

        // ---- 缺省（无计划）：完全向后兼容 ----

        [TestMethod]
        public void ComposeUserPrompt_无计划_首轮_仅问题()
        {
            var ex = NewExecutor();
            Assert.AreEqual("请问 X", ex.ComposeUserPrompt("请问 X", null, 1), "首轮应只含问题，无上轮/精化段");
        }

        [TestMethod]
        public void ComposeUserPrompt_无计划_后续轮_含上轮与精化()
        {
            var ex = NewExecutor();
            var p = ex.ComposeUserPrompt("请问 X", "上轮答案", 2);
            StringAssert.Contains(p, "[上轮回答]");
            StringAssert.Contains(p, "上轮答案");
            StringAssert.Contains(p, "[精化指令]");
        }

        // ---- UserPrompts 显式覆盖 ----

        [TestMethod]
        public void ComposeUserPrompt_UserPrompts命中_用该轮显式内容并渲染占位符()
        {
            var plan = new RoundPromptPlan { UserPrompts = new Dictionary<int, string> { [2] = "R2:{round}:{question}|{prior_answer}|{refine}" } };
            var ex = NewExecutor(plan);
            var p = ex.ComposeUserPrompt("Q", "P", 2);
            StringAssert.StartsWith(p, "R2:2:Q|P|");
            Assert.IsFalse(p.Contains("[上轮回答]"), "显式内容即用，不再套默认模板");
        }

        // ---- UserTemplate 通配回退 ----

        [TestMethod]
        public void ComposeUserPrompt_UserPrompts未命中_回退通配模板()
        {
            var plan = new RoundPromptPlan
            {
                UserPrompts = new Dictionary<int, string> { [1] = "ONLY_FIRST:{question}" },
                UserTemplate = "T:{round}:{question}",
            };
            var ex = NewExecutor(plan);
            Assert.AreEqual("ONLY_FIRST:Q", ex.ComposeUserPrompt("Q", null, 1), "命中的轮次用显式内容");
            Assert.AreEqual("T:3:Q", ex.ComposeUserPrompt("Q", "P", 3), "未命中的轮次回退通配模板");
        }

        [TestMethod]
        public void ComposeUserPrompt_未命中且无模板_回退默认()
        {
            var plan = new RoundPromptPlan { UserPrompts = new Dictionary<int, string> { [1] = "X" } };
            var ex = NewExecutor(plan);
            StringAssert.Contains(ex.ComposeUserPrompt("Q", "P", 5), "[上轮回答]");
        }

        // ---- SystemPrompts 逐轮覆盖 ----

        [TestMethod]
        public void ResolveSystem_命中轮次覆盖_未命中回退()
        {
            var plan = new RoundPromptPlan { SystemPrompts = new Dictionary<int, string> { [2] = "SYS2" } };
            var ex = NewExecutor(plan, system: "SYS");
            Assert.AreEqual("SYS", ex.ResolveSystem(1), "未配置的轮次回退执行器系统提示");
            Assert.AreEqual("SYS2", ex.ResolveSystem(2), "配置的轮次用显式系统提示");
        }

        // ---- 单轮执行端到端：system + user 均按轮次送入 ----

        [TestMethod]
        public async Task ExecuteRoundAsync_逐轮system与user送入LLM()
        {
            var fake = new FakeLlmClient();
            var plan = new RoundPromptPlan
            {
                SystemPrompts = new Dictionary<int, string> { [1] = "S1", [2] = "S2" },
                UserPrompts = new Dictionary<int, string> { [1] = "U1:{question}", [2] = "U2:{round}:{prior_answer}" },
            };
            var ex = new StatelessAnswerExecutor(fake, systemPrompt: "SYS", roundPromptPlan: plan);

            await ex.ExecuteRoundAsync("Q", priorAnswer: null, round: 1).ConfigureAwait(false);
            await ex.ExecuteRoundAsync("Q", priorAnswer: "PREV", round: 2).ConfigureAwait(false);

            Assert.AreEqual(2, fake.Calls.Count, "应恰好两轮 LLM 调用");
            Assert.AreEqual(("S1", "U1:Q"), (fake.Calls[0].System, fake.Calls[0].User), "第一轮用计划 system+user");
            Assert.AreEqual(("S2", "U2:2:PREV"), (fake.Calls[1].System, fake.Calls[1].User), "第二轮用计划 system+user");
        }

        /// <summary>记录每次调用的 system/user 的假 LLM 客户端（零成本，返回 user 作为回答）。</summary>
        private sealed class FakeLlmClient : ILlmClient
        {
            public string Name => "fake";
            public List<(string System, string User)> Calls { get; } = new();

            public Task<LlmResult> ChatAsync(
                string system, string user, IReadOnlyList<LlmMessage>? history = null, CancellationToken ct = default)
            {
                Calls.Add((system, user));
                return Task.FromResult(new LlmResult(user));
            }
        }
    }
}
