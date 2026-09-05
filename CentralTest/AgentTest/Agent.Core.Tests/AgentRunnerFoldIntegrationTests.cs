using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent.Axioms;
using Agent.Contract;
using Agent.Contract.Llm;
using Agent.Core.Signal;
using Agent.Orchestration.SessionWorkflow;
using Modules.InternalStorage.Core;
using Operation.HierarchyMethod.Nodes.Workflows;
using Operation.StateBlock.Core;
using Share.HierarchySupport;
using Share.TaskStatusPool.Loops;
using WorkFlowMethod.LoopFramework.Axioms;

namespace Agent.Core.Tests
{
    /// <summary>
    /// AgentRunner × 信号驱动折叠 整合测试（阶段四骨架退役的桥）：
    /// 验证把 <see cref="SignalWorkflowRunner"/>（折叠内核 <see cref="FoldConversationRunner"/>）作为
    /// <see cref="IWorkflowRunner"/> 注入 <see cref="AgentRunner"/> 后，主循环能正确装配、消费折叠结果并以成功（TS）终止。
    /// 保持骨架默认（不切换 AgentRunner 主循环），仅验证「换 IWorkflowRunner 实现 = 换执行内核」的迁移就绪性（SingleImplementation / TH-12）。
    /// 零真实 LLM（假客户端）：SchedulingMode.Deterministic（NoopStateDirector）驱动，主循环全确定性可跑。
    /// </summary>
    [TestClass]
    public class AgentRunnerFoldIntegrationTests
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

        /// <summary>
        /// 折叠成功（run_to_end 收敛 stop）→ AgentRunner 主循环应以成功（TS）终止，且产出折叠终答文本。
        /// 该断言固化了「折叠结果须能驱动主循环成功终止」的迁移前置条件（决策词须经 Outputs[RoundKeys.Decision] 回传）。
        /// </summary>
        [TestMethod]
        public async Task RunAsync_WithFoldRunner_Success_TerminatesAsSuccess()
        {
            var stateCenter = new StateCenter(new StorageCell("agent-runner-fold"));
            var op = new SkConversationFoldOperator(new FakeLlm());
            var foldRunner = SignalWorkflowRunner.FromFold(TwoStepWorkflow(), op, maxSteps: 8);
            var input = new AgentRunnerAssemblyInput(
                LoopConfig: new AgentLoopConfig { SchedulingMode = SchedulingMode.Deterministic },
                StateCenter: stateCenter,
                Runner: foldRunner);
            var runner = AgentRunnerFactory.Create(input);

            var item = AgentRunner.CreateItem("hello", schedulingMode: SchedulingMode.Deterministic);
            var result = await runner.RunAsync(item, TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);

            // 折叠成功 → 主循环应判成功（TS）终止，而非持续迭代至预算耗尽。
            Assert.AreEqual(LoopDecision.Success, result.FinalDecision, $"折叠成功应使主循环以 Success 终止，实际 {result.FinalDecision}");
            Assert.IsTrue(result.Rounds.Count >= 1, "主循环应至少完成一轮");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Rounds[0].Result.Text), "首轮应产出折叠终答文本");
        }

        /// <summary>
        /// 对照基线：骨架默认（不注入 Runner → 回退骨架 WorkflowRunner）时，AgentRunner 仍可装配且能跑主循环。
        /// 保证「补整合测试」不改变既有骨架路径（非破坏）。
        /// </summary>
        [TestMethod]
        public void Create_WithoutRunner_StillFallsBackToSkeleton()
        {
            var stateCenter = new StateCenter(new StorageCell("agent-runner-skeleton"));
            var input = new AgentRunnerAssemblyInput(
                LoopConfig: new AgentLoopConfig(),
                StateCenter: stateCenter);
            var runner = AgentRunnerFactory.Create(input);
            Assert.IsNotNull(runner, "缺省 Runner 应回退骨架 WorkflowRunner，装配仍成功");
        }
    }
}
