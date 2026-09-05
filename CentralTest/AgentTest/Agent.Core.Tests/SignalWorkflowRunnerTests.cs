using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent.Axioms;
using Agent.Contract;
using Agent.Orchestration.SessionWorkflow;
using Share.TaskStatusPool.Loops;
using WorkFlowMethod.LoopFramework.Axioms;

namespace Agent.Core.Tests
{
    /// <summary>
    /// <see cref="SignalWorkflowRunner"/> 适配器测试：把信号折叠委托（<see cref="FoldConversationRunner.Build"/> 形态）
    /// 适配到 <see cref="IWorkflowRunner"/> 端口，状态透传、结果返回（端口签名不变，零 LLM、纯 CI 可跑）。
    /// </summary>
    [TestClass]
    public class SignalWorkflowRunnerTests
    {
        [TestMethod]
        public async Task RunRoundAsync_ForwardsStateToFold_ReturnsResult()
        {
            AxiomContext? received = null;
            var runner = new SignalWorkflowRunner((state, ct) =>
            {
                received = state;
                return Task.FromResult(new WorkflowRoundResult(
                    true, null, "产出", "stop", LoopDecision.Success,
                    new Dictionary<string, object>()));
            });

            var state = new AxiomContext();
            state.Data[RoundKeys.UserInput] = "消息";
            var result = await runner.RunRoundAsync(state, CancellationToken.None);

            Assert.AreSame(state, received, "折叠委托应收到调用方传入的 AxiomContext");
            Assert.IsTrue(result.Ok);
            Assert.AreEqual("产出", result.Text);
        }

        [TestMethod]
        public async Task RunRoundAsync_WithSpec_PopulatesNodesFromOutputs()
        {
            var spec = new RoundSpec("conversation-2", new RoundStep[]
            {
                new("collect", new[] { "user_input" }, new[] { "collect_result" }),
                new("analyze", new[] { "collect_result" }, new[] { "analyze_result" }),
            });
            var runner = new SignalWorkflowRunner((state, ct) =>
                Task.FromResult(new WorkflowRoundResult(
                    true, null, "终答", "stop", LoopDecision.Success,
                    new Dictionary<string, object> { ["collect_result"] = "A", ["analyze_result"] = "B" })),
                spec);

            var result = await runner.RunRoundAsync(new AxiomContext(), CancellationToken.None);

            // AgentRunner 节点编排/终答归集消费 Nodes（按 spec.Steps + 折叠 Outputs 重建）。
            Assert.AreEqual(2, result.Nodes.Count);
            Assert.AreEqual("collect", result.Nodes[0].NodeKey);
            Assert.AreEqual("A", result.Nodes[0].OutputPorts["collect_result"]);
            Assert.AreEqual("analyze", result.Nodes[1].NodeKey);
            Assert.AreEqual("B", result.Nodes[1].OutputPorts["analyze_result"]);
        }

        [TestMethod]
        public async Task RunRoundAsync_WithoutSpec_LeavesNodesEmpty()
        {
            var runner = new SignalWorkflowRunner((state, ct) =>
                Task.FromResult(new WorkflowRoundResult(
                    true, null, "终答", "stop", LoopDecision.Success,
                    new Dictionary<string, object> { ["collect_result"] = "A" })));

            var result = await runner.RunRoundAsync(new AxiomContext(), CancellationToken.None);

            Assert.AreEqual(0, result.Nodes.Count);
        }
    }
}
