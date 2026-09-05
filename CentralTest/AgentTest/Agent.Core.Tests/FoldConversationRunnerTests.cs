using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent.Axioms;
using Agent.Contract;
using Agent.Contract.Llm;
using Agent.Core.Signal;
using Agent.Orchestration.SessionWorkflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.HierarchyMethod.Nodes.Workflows;
using WorkFlowMethod.LoopFramework.Axioms;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 信号驱动会话折叠 runner（<see cref="FoldConversationRunner"/>）测试：spec.Steps → chain 转移表、
    /// 折叠结果 → <see cref="WorkflowRoundResult"/> 映射（终答/分层气泡 Outputs/成败）。零真实 LLM（假客户端）。
    /// 验证「保持 _runnerFactory 端口不变，换执行内核 = 换 runner」的编排层零改动整合（SingleImplementation / TH-12）。
    /// </summary>
    [TestClass]
    public class FoldConversationRunnerTests
    {
        private sealed class FakeLlm : ILlmClient
        {
            public string Name => "fake";

            public Task<LlmResult> ChatAsync(
                string system, string user, IReadOnlyList<LlmMessage>? history = null, CancellationToken ct = default)
                => Task.FromResult(new LlmResult($"产出:{user}"));
        }

        private static RoundSpec TwoStepWorkflow() => new("conversation-2", new RoundStep[]
        {
            new("collect", new[] { "user_input" }, new[] { "collect_result" }),
            new("analyze", new[] { "collect_result" }, new[] { "analyze_result" }),
        });

        private static AxiomContext Lease(string text)
        {
            var lease = new AxiomContext();
            lease.Data[RoundKeys.UserInput] = text;
            return lease;
        }

        [TestMethod]
        public void BuildChain_TwoSteps_ProducesStartToLinearChain()
        {
            var chain = FoldConversationRunner.BuildChain(TwoStepWorkflow());

            Assert.AreEqual("collect", chain["start"]);
            Assert.AreEqual("analyze", chain["collect"]);
            // 链终点：末步无 on_success → resolver 返回 null → 收敛。
            Assert.IsFalse(chain.ContainsKey("analyze"));
        }

        [TestMethod]
        public void BuildChain_EmptySteps_Throws()
        {
            var empty = new RoundSpec("empty", new RoundStep[0]);
            var threw = false;
            try { FoldConversationRunner.BuildChain(empty); }
            catch (System.ArgumentException) { threw = true; }
            Assert.IsTrue(threw, "空步骤工作流应抛出 ArgumentException");
        }

        [TestMethod]
        public async Task Build_Runner_AlongChain_MapsFoldResultToWorkflowRoundResult()
        {
            var runner = FoldConversationRunner.Build(TwoStepWorkflow(), new SkConversationFoldOperator(new FakeLlm()));

            var result = await runner(Lease("hello"), default).ConfigureAwait(false);

            Assert.IsTrue(result.IsSuccessful, $"折叠应成功，实际 Cut 对应 Error={result.Error}");
            Assert.IsNull(result.Error);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Text), "终答应为末步产出");
            // 分层气泡：每步按 spec.Steps.ResultKeys[0] 填充 Outputs（走 ExtractStages 回退路径）。
            Assert.IsTrue(result.Outputs.ContainsKey("collect_result"));
            Assert.IsTrue(result.Outputs.ContainsKey("analyze_result"));
            Assert.IsTrue(result.Outputs["collect_result"]!.ToString()!.StartsWith("产出:"));
        }

        [TestMethod]
        public async Task Build_Runner_BudgetExhausted_ReportsResourceError()
        {
            // maxSteps=1：链 2 步 → 预算耗尽 → FoldCut.BudgetExhausted → 资源耗尽错误归因（编排层 D1 完成信封处理）。
            var runner = FoldConversationRunner.Build(TwoStepWorkflow(), new SkConversationFoldOperator(new FakeLlm()), maxSteps: 1);

            var result = await runner(Lease("hello"), default).ConfigureAwait(false);

            Assert.IsFalse(result.IsSuccessful);
            Assert.IsNotNull(result.Error);
            Assert.AreEqual(ErrorClass.ResourceExhausted, result.ErrorClass);
        }

        [TestMethod]
        public async Task Build_Runner_Success_PopulatesDecisionAndExecResult()
        {
            var runner = FoldConversationRunner.Build(TwoStepWorkflow(), new SkConversationFoldOperator(new FakeLlm()));

            var result = await runner(Lease("hello"), default).ConfigureAwait(false);

            // 决策信号化：run_to_end 收敛成功 ↔ stop（DecisionWordClassifier 单一权威）。
            Assert.AreEqual("stop", result.DecisionWord);
            Assert.AreEqual(LoopDecision.Success, result.Decision);
            // 执行结果信封：AgentRunner BuildSignal 消费 ExecResult（成功携带终答文本）。
            Assert.IsNotNull(result.ExecResult);
            Assert.IsTrue(result.ExecResult!.Ok);
            Assert.AreEqual(result.Text, result.ExecResult.Text);
            // 决策词回写 Outputs[RoundKeys.Decision]（阶段四迁移前置）：AgentRunner 主循环 MapEval 判 Pass 依赖该键，
            // 缺失则折叠成功也会被判 Continue 持续迭代至预算耗尽。
            Assert.AreEqual("stop", result.Outputs[RoundKeys.Decision]);
        }
    }
}
