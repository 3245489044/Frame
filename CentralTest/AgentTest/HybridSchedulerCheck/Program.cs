using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Agent.Contract.Llm;
using Agent.Contract.Scheduling;
using Agent.Axioms.Scheduling;
using Agent.State.Runtime;
using Agent.Core.TaskPool;
using WorkFlowMethod.LoopFramework.Axioms;
using Share.AiScheduling;
using Share.StateManagement;
using Share.TaskStatusPool.Loops;
using Share.TaskStatusPool.TaskPool;

namespace HybridSchedulerCheck
{
    /// <summary>
    /// 运行时验证入口：执行混合调度（LLM + 计算）与非限定快照驱动循环的关键场景，全部通过返回 0。
    /// 用作临时验证载体（Agent.Core.Tests 项目存在既有的 DynamicWorkflowMerger / Agent.Planner 缺失问题，暂不可编译运行）。
    /// </summary>
    public static class Program
    {
        private static int _failed;

        private sealed class StubDirector : IStateDirector
        {
            private readonly StateTransitionProposal? _proposal;
            public StubDirector(StateTransitionProposal? proposal) => _proposal = proposal;
            public Task<StateTransitionProposal?> DecideAsync(SchedulerDirectorContext c, CancellationToken ct = default)
                => Task.FromResult(_proposal);
        }

        private static AgentTaskItem NewState()
        {
            var s = new AgentTaskItem { Round = 1, CurrentStrategyId = "A" };
            s.AvailableStrategyIds.Add("A");
            s.AvailableStrategyIds.Add("B");
            s.AgentStatus["ex1"] = AgentRunStatus.Pending.ToString();
            return s;
        }

        public static async Task<int> Main()
        {
            GuardTests();
            await MachineTests();
            DirectorTests();
            LoopTests();
            HybridLoopTests();
            BudgetTerminationTests();
            await Phase3InvariantTests();
            AlgorithmOnSnapshotTests();
            SerializerTests();

            Console.WriteLine(_failed == 0 ? "ALL PASS" : $"{_failed} FAILED");
            return _failed == 0 ? 0 : 1;
        }

        private static void Check(string name, bool cond)
        {
            if (cond) Console.WriteLine($"  [PASS] {name}");
            else { _failed++; Console.WriteLine($"  [FAIL] {name}"); }
        }

        private static void GuardTests()
        {
            Console.WriteLine("== TransitionGuard ==");
            var guard = new TransitionGuard();
            var ctx = new AiSchedulerStateMachine<AgentTaskItem>(new StubDirector(null), guard, NewState()).BuildContext();
            Check("reject unknown strategy",
                guard.Verify(new StateTransitionProposal(SchedulerAction.SwitchStrategy, TargetStrategyId: "X"), ctx) == TransitionVerdict.Reject);
            Check("allow known strategy switch",
                guard.Verify(new StateTransitionProposal(SchedulerAction.SwitchStrategy, TargetStrategyId: "B"), ctx) == TransitionVerdict.Pass);
            Check("same strategy held",
                guard.Verify(new StateTransitionProposal(SchedulerAction.SwitchStrategy, TargetStrategyId: "A"), ctx) == TransitionVerdict.Held);
            Check("reject illegal run status Pending→Done",
                guard.Verify(new StateTransitionProposal(SchedulerAction.AdvanceRun, TargetExecutorId: "ex1", TargetRunStatusId: AgentRunStatus.Done.ToString()), ctx) == TransitionVerdict.Reject);
            Check("allow legal run status Pending→Running",
                guard.Verify(new StateTransitionProposal(SchedulerAction.AdvanceRun, TargetExecutorId: "ex1", TargetRunStatusId: AgentRunStatus.Running.ToString()), ctx) == TransitionVerdict.Pass);
        }
        private static async Task MachineTests()
        {
            Console.WriteLine("== AiSchedulerStateMachine ==");
            var state = NewState();
            var machine = new AiSchedulerStateMachine<AgentTaskItem>(
                new StubDirector(new StateTransitionProposal(SchedulerAction.SwitchStrategy, TargetStrategyId: "B", Reason: "r")),
                new TransitionGuard(), state);
            var decision = await machine.AdvanceAsync();
            Check("applies valid strategy switch", decision.Applied && decision.Verdict == TransitionVerdict.Pass && state.CurrentStrategyId == "B");

            var s2 = NewState();
            var m2 = new AiSchedulerStateMachine<AgentTaskItem>(
                new StubDirector(new StateTransitionProposal(SchedulerAction.AdvanceRun, TargetExecutorId: "ex1", TargetRunStatusId: AgentRunStatus.Done.ToString())),
                new TransitionGuard(), s2);
            var d2 = await m2.AdvanceAsync();
            Check("reject illegal run status falls back", !d2.Applied && d2.Verdict == TransitionVerdict.Reject && s2.AgentStatus["ex1"] == AgentRunStatus.Pending.ToString());

            var s3 = NewState();
            var m3 = new AiSchedulerStateMachine<AgentTaskItem>(new StubDirector(null), new TransitionGuard(), s3);
            var d3 = await m3.AdvanceAsync();
            Check("director null stays", !d3.Applied && d3.Verdict == TransitionVerdict.Held && s3.CurrentStrategyId == "A");

            var s4 = NewState();
            var m4 = new AiSchedulerStateMachine<AgentTaskItem>(
                new StubDirector(new StateTransitionProposal(SchedulerAction.Explore, Intent: "try different angle", Reason: "explore")),
                new TransitionGuard(), s4);
            var d4 = await m4.AdvanceAsync();
            Check("explore allowed (free exploration)", d4.Applied && d4.Verdict == TransitionVerdict.Pass);
        }

        private static void DirectorTests()
        {
            Console.WriteLine("== LlmStateDirector ==");
            var ctx = new AiSchedulerStateMachine<AgentTaskItem>(new StubDirector(null), new TransitionGuard(), NewState()).BuildContext();

            var d1 = new LlmStateDirector(jsonGenerator: (s, u, t, ct) =>
                Task.FromResult(new LlmResult("{\"action\":\"switch_strategy\",\"target_strategy\":\"B\",\"reason\":\"x\",\"confidence\":0.8}")));
            var p1 = d1.DecideAsync(ctx, CancellationToken.None).GetAwaiter().GetResult();
            Check("parses switch proposal", p1 is { Action: SchedulerAction.SwitchStrategy, TargetStrategyId: "B" });

            var d2 = new LlmStateDirector(jsonGenerator: (s, u, t, ct) => Task.FromResult(new LlmResult("not json")));
            var p2 = d2.DecideAsync(ctx, CancellationToken.None).GetAwaiter().GetResult();
            Check("returns null on bad json", p2 is null);

            var d3 = new LlmStateDirector(jsonGenerator: (s, u, t, ct) =>
                Task.FromResult(new LlmResult("{\"action\":\"switch_strategy\",\"target_strategy\":\"X\",\"confidence\":0.9}")));
            var p3 = d3.DecideAsync(ctx, CancellationToken.None).GetAwaiter().GetResult();
            Check("rejects out-of-scope strategy", p3 is null);
        }

        private static void LoopTests()
        {
            Console.WriteLine("== SnapshotDrivenLoopEngine ==");
            var engine = new SnapshotDrivenLoopEngine<int, int>();
            int rounds = 0;
            var result = engine.RunAsync(
                initialState: 0,
                execute: (s, ct) => { rounds++; return Task.FromResult(s + 1); },
                next: (s, r, ct) => Task.FromResult(s + 1),
                ct: CancellationToken.None,
                externalTerminate: (s, ct) => Task.FromResult(rounds >= 3)).GetAwaiter().GetResult();
            Check("non-restrictive: no internal max-iterations, external terminate stops",
                result.Rounds.Count == 3 && result.TerminatedByExternal && result.FinalState == 3);

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var r2 = engine.RunAsync(0, (s, ct) => Task.FromResult(0), (s, r, ct) => Task.FromResult(s), cts.Token).GetAwaiter().GetResult();
            Check("stops by cancellation", r2.Rounds.Count == 0 && r2.TerminatedByExternal);
        }

        /// <summary>端到端：非限定快照驱动循环 + 混合状态机（LLM 提议 + 计算守卫）共同驱动整体状态变更。</summary>
        private static void HybridLoopTests()
        {
            Console.WriteLine("== Hybrid + Loop integration ==");
            var state = new AgentTaskItem { Round = 1, CurrentStrategyId = "A" };
            state.Token.Max = 3;
            state.AvailableStrategyIds.Add("A");
            state.AvailableStrategyIds.Add("B");
            state.AgentStatus["ex1"] = AgentRunStatus.Running.ToString();

            var machine = new AiSchedulerStateMachine<AgentTaskItem>(
                new StubDirector(new StateTransitionProposal(SchedulerAction.SwitchStrategy, TargetStrategyId: "B", Reason: "switch")),
                new TransitionGuard(), state);

            var engine = new SnapshotDrivenLoopEngine<AgentTaskItem, string>();
            var result = engine.RunAsync(
                initialState: state,
                execute: (s, ct) => Task.FromResult("round"),
                next: async (s, r, ct) => { await machine.AdvanceAsync(); s.Round++; return s; },
                ct: CancellationToken.None,
                externalTerminate: (s, ct) => Task.FromResult(s.Round > 3)).GetAwaiter().GetResult();

            Check("hybrid loop switched strategy to B", state.CurrentStrategyId == "B");
            Check("hybrid loop ran 3 rounds (non-restrictive)", result.Rounds.Count == 3);
        }

        /// <summary>终止二分：预算终止（可判定）由引擎内 budgetTerminate 裁决，阈值到达即触发、不外置（AX.TerminationBipartition.Budgetary）。</summary>
        private static void BudgetTerminationTests()
        {
            Console.WriteLine("== Budget termination (in-engine, AX.TerminationBipartition) ==");
            var engine = new SnapshotDrivenLoopEngine<AgentTaskItem, string>();

            // 初始快照已处终态（token 耗尽）→ 引擎内 budgetTerminate 直接 break，0 轮，FinalDecision=TO。
            var s1 = new AgentTaskItem { Round = 1 };
            s1.Token.Max = 3; s1.Token.Advance(); s1.Token.Advance(); s1.Token.Advance(); // value=3=exhausted
            var r1 = engine.RunAsync(
                initialState: s1,
                execute: (s, ct) => Task.FromResult("round"),
                next: (s, r, ct) => { s.Round++; return Task.FromResult(s); },
                ct: CancellationToken.None,
                budgetTerminate: st => TerminationDecider.ShouldTerminate(st, out var d) ? d : LoopDecision.Continue
                ).GetAwaiter().GetResult();
            Check("budget terminate on exhausted token: 0 rounds", r1.Rounds.Count == 0);
            Check("budget terminate on exhausted token: FinalDecision=TO", r1.FinalDecision == LoopDecision.Timeout);
            Check("budget terminate is NOT external", !r1.TerminatedByExternal);

            // halt=1 → TM，引擎内 break。
            var s2 = new AgentTaskItem { Round = 1 };
            s2.Flag.SetHalt();
            var r2 = engine.RunAsync(
                initialState: s2,
                execute: (s, ct) => Task.FromResult("round"),
                next: (s, r, ct) => { s.Round++; return Task.FromResult(s); },
                ct: CancellationToken.None,
                budgetTerminate: st => TerminationDecider.ShouldTerminate(st, out var d) ? d : LoopDecision.Continue
                ).GetAwaiter().GetResult();
            Check("budget terminate on halt: FinalDecision=TM", r2.FinalDecision == LoopDecision.TripTerminated);

            // 每轮 next 推进 token，直到耗尽 → 引擎内预算终止在【执行后】到达即触发（无需 externalTerminate）。
            var s4 = new AgentTaskItem { Round = 1 };
            s4.Token.Max = 2;
            var r4 = engine.RunAsync(
                initialState: s4,
                execute: (s, ct) => Task.FromResult("round"),
                next: (s, r, ct) => { s.Token.Advance(); s.Round++; return Task.FromResult(s); },
                ct: CancellationToken.None,
                budgetTerminate: st => TerminationDecider.ShouldTerminate(st, out var d) ? d : LoopDecision.Continue
                ).GetAwaiter().GetResult();
            Check("in-engine budget terminate after token reaches max (no externalTerminate)",
                r4.Rounds.Count == 2 && r4.FinalDecision == LoopDecision.Timeout && !r4.TerminatedByExternal);
        }

        /// <summary>阶段3不变式：确定性核心守卫（AX.StateRootSingleSource / DeterministicCoreDisjoint）、历史只增归档（InformationMonotonicity）、仲裁键。</summary>
        private static async Task Phase3InvariantTests()
        {
            Console.WriteLine("== Phase3 invariants (deterministic core / append-only history / arbitration key) ==");

            // 确定性核心不变式：混合调度 Apply 只改策略/执行状态，不触碰 Budget/Token/Flag/Round。
            var s1 = new AgentTaskItem { Round = 5, CurrentStrategyId = "A" };
            s1.AvailableStrategyIds.Add("A"); s1.AvailableStrategyIds.Add("B");
            s1.Token.Max = 3;
            var b0 = s1.Budget.Level; var t0 = s1.Token.Value; var h0 = s1.Flag.Halt; var r0 = s1.Round;
            var m1 = new AiSchedulerStateMachine<AgentTaskItem>(
                new StubDirector(new StateTransitionProposal(SchedulerAction.SwitchStrategy, TargetStrategyId: "B", Reason: "switch")),
                new TransitionGuard(), s1);
            var d1 = await m1.AdvanceAsync();
            Check("deterministic core untouched by strategy switch",
                s1.Budget.Level == b0 && s1.Token.Value == t0 && s1.Flag.Halt == h0 && s1.Round == r0);
            Check("arbitration key recorded (EngineAxis.L1)", !string.IsNullOrEmpty(d1.ArbitrationKey) && d1.ArbitrationKey.StartsWith("EngineAxis"));

            // 历史只增归档：有界遥测窗口弹出时归档到 Data[transitionHistory]，信息不丢失。
            var s2 = new AgentTaskItem { Round = 1, CurrentStrategyId = "A" };
            s2.AvailableStrategyIds.Add("A"); s2.AvailableStrategyIds.Add("B");
            var m2 = new AiSchedulerStateMachine<AgentTaskItem>(
                new StubDirector(new StateTransitionProposal(SchedulerAction.Explore, Intent: "i", Reason: "explore")),
                new TransitionGuard(), s2);
            for (var i = 0; i < 25; i++) await m2.AdvanceAsync();
            Check("recent transitions bounded to 20 (telemetry window)", s2.RecentTransitions.Count == 20);
            var archived = s2.Data.TryGet<object?>("transitionHistory", out var h) ? h?.ToString() : null;
            Check("evicted transitions archived (append-only, no loss)",
                archived != null && archived.Split('\n').Length == 5);
        }

        /// <summary>Stage A：公理引擎算法层（ILoopState 依赖）零改动直接运行在统一快照上。</summary>
        private static void AlgorithmOnSnapshotTests()
        {
            Console.WriteLine("== ILoopState algorithm on AgentTaskItem ==");
            var s = new AgentTaskItem { Round = 1, Scope = Scope.LoopBody };
            s.Token.Max = 3;

            // 算法层以 ILoopState 接收快照（无需 AxiomContext）。
            ProgressUpdater.Apply(s, EvalOutcome.Fail);                       // token+1
            BudgetEscalator.Apply(s, ErrorClass.ExecutionFailure, EvalOutcome.Fail); // budget+1
            Check("algorithm advances token on snapshot", s.Token.Value == 1);
            Check("algorithm escalates budget on snapshot", s.Budget.Level == 1);
            Check("algorithm writes LastEval", s.LastEval == EvalOutcome.Fail);

            var terminated = TerminationDecider.ShouldTerminate(s, out var d);
            Check("algorithm termination not tripped (non-restrictive)", !terminated && d == LoopDecision.Continue);

            // 快照作为 ILoopState 直接传入 RoundLifecycle 兼容签名（execute 侧不在此验证）。
            ILoopState asInterface = s;
            Check("snapshot implements ILoopState", asInterface.Budget == s.Budget && asInterface.Token == s.Token && asInterface.Flag == s.Flag);
        }

        /// <summary>统一任务快照（<see cref="AgentTaskItem"/>）PayloadJson 单一状态基座 round-trip：输入契约 + 运行时 + 生命周期 + Data 全量保留。</summary>
        private static void SerializerTests()
        {
            Console.WriteLine("== AgentTaskItem PayloadJson round-trip (单一状态基座) ==");
            var item = new AgentTaskItem { Round = 2, ContinuousFailures = 2, CurrentStrategyId = "B", Status = TaskExecutionStatus.Running };
            item.Budget.Escalate();                // level 1
            item.Token.Max = 3;
            item.Token.Advance();                  // value 1
            item.Token.Advance();                  // value 2
            item.Flag.SetHalt();
            item.AvailableStrategyIds.Add("A");
            item.AvailableStrategyIds.Add("B");
            item.AgentStatus["ex1"] = AgentRunStatus.Running.ToString();
            item.AgentStatus["ex2"] = AgentRunStatus.Done.ToString();
            item.RecentTransitions.Add("strategy:A→B");
            item.Data.Set("intent", "explore");
            item.Data.Set("count", 7L);

            var json = JsonSerializer.Serialize(item);
            var restored = JsonSerializer.Deserialize<AgentTaskItem>(json)!;

            Check("round preserved", restored.Round == 2);
            Check("budget/token/halt preserved", restored.Budget.Level == 1 && restored.Token.Value == 2 && restored.Token.Max == 3 && restored.Flag.Halt);
            Check("status preserved", restored.Status == TaskExecutionStatus.Running);
            Check("strategy preserved", restored.CurrentStrategyId == "B");
            Check("available strategies preserved", restored.AvailableStrategyIds.Count == 2 && restored.AvailableStrategyIds.Contains("B"));
            Check("agent status preserved", restored.AgentStatus["ex1"] == AgentRunStatus.Running.ToString() && restored.AgentStatus["ex2"] == AgentRunStatus.Done.ToString());
            Check("recent transitions preserved", restored.RecentTransitions.Count == 1 && restored.RecentTransitions[0] == "strategy:A→B");
            Check("data scalars preserved", restored.Data.Get<object?>("intent") is "explore" && Convert.ToInt64(restored.Data.Get<object?>("count")!) == 7L);
        }
    }
}
