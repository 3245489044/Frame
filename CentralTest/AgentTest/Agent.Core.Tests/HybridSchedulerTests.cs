using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Contract.Llm;
using Agent.Contract.Scheduling;
using Agent.Axioms.Scheduling;
using Agent.State.Runtime;
using Agent.Core.TaskPool;
using Share.AiScheduling;
using Share.StateManagement;
using Share.TaskStatusPool.TaskPool;
using TestInfrastructure;

namespace Agent.Core.Tests
{
    [TestClass]
    [TestCategory(TestCategories.Concurrency)]
    public class HybridSchedulerTests
    {
        // ---- 测试辅助：固定提议的 mock 导演（自由探索源替身） ----
        private sealed class StubDirector : IStateDirector
        {
            private readonly StateTransitionProposal? _proposal;
            public int CallCount;
            public StubDirector(StateTransitionProposal? proposal) => _proposal = proposal;
            public Task<StateTransitionProposal?> DecideAsync(SchedulerDirectorContext c, CancellationToken ct = default)
            { CallCount++; return Task.FromResult(_proposal); }
        }

        private static AgentTaskItem NewState()
        {
            var s = new AgentTaskItem { Round = 1, CurrentStrategyId = "A", Status = TaskExecutionStatus.Running };
            s.AvailableStrategyIds.Add("A");
            s.AvailableStrategyIds.Add("B");
            s.AgentStatus["ex1"] = AgentRunStatus.Pending.ToString();
            return s;
        }

        // ---- 计算层：TransitionGuard（策略完全实施）----

        [TestMethod]
        [Timeout(15000)]
        public void TransitionGuard_RejectsUnknownStrategy()
        {
            var guard = new TransitionGuard();
            var ctx = new AiSchedulerStateMachine<AgentTaskItem>(new StubDirector(null), guard, NewState()).BuildContext();
            var proposal = new StateTransitionProposal(SchedulerAction.SwitchStrategy, TargetStrategyId: "X");
            Assert.AreEqual(TransitionVerdict.Reject, guard.Verify(proposal, ctx));
        }

        [TestMethod]
        [Timeout(15000)]
        public void TransitionGuard_AllowsKnownStrategySwitch()
        {
            var guard = new TransitionGuard();
            var ctx = new AiSchedulerStateMachine<AgentTaskItem>(new StubDirector(null), guard, NewState()).BuildContext();
            Assert.AreEqual(TransitionVerdict.Pass, guard.Verify(
                new StateTransitionProposal(SchedulerAction.SwitchStrategy, TargetStrategyId: "B"), ctx));
            Assert.AreEqual(TransitionVerdict.Held, guard.Verify(
                new StateTransitionProposal(SchedulerAction.SwitchStrategy, TargetStrategyId: "A"), ctx));
        }

        [TestMethod]
        [Timeout(15000)]
        public void TransitionGuard_RejectsIllegalRunStatus()
        {
            var guard = new TransitionGuard();
            var ctx = new AiSchedulerStateMachine<AgentTaskItem>(new StubDirector(null), guard, NewState()).BuildContext();
            // Pending → Done 非法（Pending 仅能到 Running）。
            Assert.AreEqual(TransitionVerdict.Reject, guard.Verify(
                new StateTransitionProposal(SchedulerAction.AdvanceRun, TargetExecutorId: "ex1", TargetRunStatusId: AgentRunStatus.Done.ToString()), ctx));
            // Pending → Running 合法。
            Assert.AreEqual(TransitionVerdict.Pass, guard.Verify(
                new StateTransitionProposal(SchedulerAction.AdvanceRun, TargetExecutorId: "ex1", TargetRunStatusId: AgentRunStatus.Running.ToString()), ctx));
        }
        // ---- 混合机制：HybridSchedulerStateMachine（LLM 提议 + 计算守卫 → 原子施加）----

        [TestMethod]
        [Timeout(15000)]
        public async Task HybridMachine_AppliesValidStrategySwitch()
        {
            var state = NewState();
            var machine = new AiSchedulerStateMachine<AgentTaskItem>(
                new StubDirector(new StateTransitionProposal(SchedulerAction.SwitchStrategy, TargetStrategyId: "B", Reason: "r")),
                new TransitionGuard(), state);
            var decision = await machine.AdvanceAsync();
            Assert.IsTrue(decision.Applied);
            Assert.AreEqual(TransitionVerdict.Pass, decision.Verdict);
            Assert.AreEqual("B", state.CurrentStrategyId);
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task HybridMachine_RejectFallsBackOnIllegalRunStatus()
        {
            var state = NewState();
            var machine = new AiSchedulerStateMachine<AgentTaskItem>(
                new StubDirector(new StateTransitionProposal(
                    SchedulerAction.AdvanceRun, TargetExecutorId: "ex1", TargetRunStatusId: AgentRunStatus.Done.ToString())),
                new TransitionGuard(), state);
            var decision = await machine.AdvanceAsync();
            Assert.IsFalse(decision.Applied);
            Assert.AreEqual(TransitionVerdict.Reject, decision.Verdict);
            Assert.AreEqual(AgentRunStatus.Pending.ToString(), state.AgentStatus["ex1"]); // 未变
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task HybridMachine_DirectorNull_Stays()
        {
            var state = NewState();
            var machine = new AiSchedulerStateMachine<AgentTaskItem>(new StubDirector(null), new TransitionGuard(), state);
            var decision = await machine.AdvanceAsync();
            Assert.IsFalse(decision.Applied);
            Assert.AreEqual(TransitionVerdict.Held, decision.Verdict);
            Assert.AreEqual("A", state.CurrentStrategyId);
        }

        // ---- LLM 导演：LlmStateDirector（自由探索源，fail-safe）----

        [TestMethod]
        [Timeout(15000)]
        public void LlmDirector_ParsesSwitchProposal()
        {
            var director = new LlmStateDirector(jsonGenerator: (s, u, t, ct) =>
                Task.FromResult(new LlmResult("{\"action\":\"switch_strategy\",\"target_strategy\":\"B\",\"reason\":\"x\",\"confidence\":0.8}")));
            var ctx = new AiSchedulerStateMachine<AgentTaskItem>(new StubDirector(null), new TransitionGuard(), NewState()).BuildContext();
            var proposal = director.DecideAsync(ctx, CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsNotNull(proposal);
            Assert.AreEqual(SchedulerAction.SwitchStrategy, proposal.Action);
            Assert.AreEqual("B", proposal.TargetStrategyId);
        }

        [TestMethod]
        [Timeout(15000)]
        public void LlmDirector_ReturnsNullOnBadJson()
        {
            var director = new LlmStateDirector(jsonGenerator: (s, u, t, ct) =>
                Task.FromResult(new LlmResult("not json")));
            var ctx = new AiSchedulerStateMachine<AgentTaskItem>(new StubDirector(null), new TransitionGuard(), NewState()).BuildContext();
            var proposal = director.DecideAsync(ctx, CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsNull(proposal);
        }

        [TestMethod]
        [Timeout(15000)]
        public void LlmDirector_RejectsOutOfScopeStrategy()
        {
            var director = new LlmStateDirector(jsonGenerator: (s, u, t, ct) =>
                Task.FromResult(new LlmResult("{\"action\":\"switch_strategy\",\"target_strategy\":\"X\",\"confidence\":0.9}")));
            var ctx = new AiSchedulerStateMachine<AgentTaskItem>(new StubDirector(null), new TransitionGuard(), NewState()).BuildContext();
            var proposal = director.DecideAsync(ctx, CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsNull(proposal); // 可用集之外 → fail-safe null
        }
    }
}
