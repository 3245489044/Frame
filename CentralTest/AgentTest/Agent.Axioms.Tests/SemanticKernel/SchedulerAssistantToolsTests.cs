using System.Collections.Generic;
using System.Threading.Tasks;
using Agent.Axioms.SemanticKernel.Functions;
using Agent.Contract.Scheduling;
using Agent.State;
using BasicMethod.Functor;
using Microsoft.SemanticKernel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.AiScheduling;
using Share.StateManagement;

namespace Agent.Axioms.Tests.SemanticKernel
{
    /// <summary>
    /// S2 冒烟测试：LLM 辅助工具（SchedulerAssistantTools）经 SkFunctionBinder 注册后可经 kernel.InvokeAsync 调用，
    /// 从 Functor 透传的调度上下文读取只读快照（无上下文 fail-safe 返回 "(不可用)"）。
    /// 验证 Function Calling 能力层 + 动态上下文透传链路。
    /// </summary>
    [TestClass]
    public sealed class SchedulerAssistantToolsTests
    {
        private static SchedulerDirectorContext NewContext()
            => new(
                CurrentStrategyId: "standard",
                AvailableStrategyIds: new[] { "standard", "aggressive" },
                Telemetry: new SnapshotTelemetry(
                    Round: 1, BudgetLevel: 2, TokenValue: 3, TokenMax: 5,
                    Halt: false, ContinuousFailures: 0, LastEval: "pass", LastErrorClass: null),
                AgentStatus: new Dictionary<string, string>(),
                RecentTransitions: new List<string>());

        [TestMethod]
        public async Task QueryAvailableStrategies_NoContext_FailsSafe()
        {
            var kernel = Kernel.CreateBuilder().Build();
            var result = await kernel.InvokeAsync(SchedulerAssistantTools.QueryAvailableStrategies);
            Assert.AreEqual("(不可用)", result.GetValue<string>());
        }

        [TestMethod]
        public async Task QueryAvailableStrategies_WithContext_ReturnsList()
        {
            using var rc = new RunContext(default);
            using var enter = RunContext.Enter(rc);
            rc.SchedulerContext = NewContext();
            var kernel = Kernel.CreateBuilder().Build();
            var result = await kernel.InvokeAsync(SchedulerAssistantTools.QueryAvailableStrategies);
            Assert.AreEqual("standard, aggressive", result.GetValue<string>());
        }

        [TestMethod]
        public async Task QueryTelemetrySnapshot_WithContext_ReturnsSnapshot()
        {
            using var rc = new RunContext(default);
            using var enter = RunContext.Enter(rc);
            rc.SchedulerContext = NewContext();
            var kernel = Kernel.CreateBuilder().Build();
            var result = await kernel.InvokeAsync(SchedulerAssistantTools.QueryTelemetrySnapshot);
            var text = result.GetValue<string>()!;
            Assert.IsTrue(text.Contains("round=1"), text);
            Assert.IsTrue(text.Contains("budgetLevel=2"), text);
            Assert.IsTrue(text.Contains("lastEval=pass"), text);
        }

        [TestMethod]
        public void RunContext_SchedulerContextRoundTrip()
        {
            using var rc = new RunContext(default);
            using var enter = RunContext.Enter(rc);
            rc.SchedulerContext = NewContext();
            Assert.IsNotNull(AgentRunCancellation.GetSchedulerContext());
            Assert.AreEqual("standard", AgentRunCancellation.GetSchedulerContext()!.CurrentStrategyId);
        }
    }
}
