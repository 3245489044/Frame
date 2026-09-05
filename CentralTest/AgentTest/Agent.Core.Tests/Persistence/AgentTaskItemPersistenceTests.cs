using Agent.Contract.Answer;
using Agent.Contract.Scheduling;
using Agent.Core.TaskPool;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.TaskStatusPool.Loops;
using Share.TaskStatusPool.TaskPool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WorkFlowMethod.LoopFramework.Axioms;

namespace Agent.Core.Tests
{
    [TestClass]
    public class AgentTaskItemSerializationTests
    {
        private static AgentTaskItem MakeItem()
        {
            var item = new AgentTaskItem
            {
                TaskKey = "k-1",
                Priority = 5,
                MaxRetries = 2,
                UserInput = "hi",
                ThreadId = "t",
                InputKey = "in",
                SchedulingMode = SchedulingMode.Hybrid,
                SchedulerRulesKey = "rules",
                Round = 2,
                ContinuousFailures = 2,
                CurrentStrategyId = "B",
                Status = TaskExecutionStatus.Running,
                Scope = Scope.LoopBody,
                LastEval = EvalOutcome.Partial,
                LastResultText = "text",
            };
            item.Budget.Escalate();                // level 1
            item.Token.Max = 3;
            item.Token.Advance(); item.Token.Advance(); // value 2
            item.Flag.SetHalt();
            item.AvailableStrategyIds.Add("A");
            item.AvailableStrategyIds.Add("B");
            item.AgentStatus["ex1"] = "Running";
            item.RecentTransitions.Add("a→b");
            item.Data.Set("intent", "explore");
            item.Data.Set("count", 7L);
            return item;
        }

        [TestMethod]
        public void RoundTrip_PreservesAllStateBaseFields()
        {
            var item = MakeItem();
            var json = JsonSerializer.Serialize(item);
            var restored = JsonSerializer.Deserialize<AgentTaskItem>(json)!;

            // 输入契约（输入端）
            Assert.AreEqual("k-1", restored.TaskKey);
            Assert.AreEqual(5, restored.Priority);
            Assert.AreEqual(2, restored.MaxRetries);
            Assert.AreEqual("hi", restored.UserInput);
            Assert.AreEqual("t", restored.ThreadId);
            Assert.AreEqual("in", restored.InputKey);
            Assert.AreEqual(SchedulingMode.Hybrid, restored.SchedulingMode);
            Assert.AreEqual("rules", restored.SchedulerRulesKey);

            // 运行时核心（ILoopState）
            Assert.AreEqual(2, restored.Round);
            Assert.AreEqual(1, restored.Budget.Level);
            Assert.AreEqual(2, restored.Token.Value);
            Assert.AreEqual(3, restored.Token.Max);
            Assert.IsTrue(restored.Flag.Halt);
            Assert.AreEqual(EvalOutcome.Partial, restored.LastEval);
            Assert.AreEqual("text", restored.LastResultText);

            // 生命周期 / Agent 扩展
            Assert.AreEqual(TaskExecutionStatus.Running, restored.Status);
            Assert.AreEqual(2, restored.ContinuousFailures);
            Assert.AreEqual("B", restored.CurrentStrategyId);
            CollectionAssert.AreEqual(new[] { "A", "B" }, restored.AvailableStrategyIds);
            Assert.AreEqual("Running", restored.AgentStatus["ex1"]);
            CollectionAssert.AreEqual(new[] { "a→b" }, restored.RecentTransitions);

            // 数据空间
            Assert.AreEqual("explore", restored.Data.Get<object?>("intent"));
            Assert.AreEqual(7L, Convert.ToInt64(restored.Data.Get<object?>("count")));
        }

        [TestMethod]
        public void AsILoopState_AlgorithmAdvancesCore()
        {
            var item = new AgentTaskItem { Round = 1, Scope = Scope.LoopBody };
            item.Token.Max = 3;
            ILoopState s = item;

            ProgressUpdater.Apply(s, EvalOutcome.Fail);                                        // token+1
            BudgetEscalator.Apply(s, ErrorClass.ExecutionFailure, EvalOutcome.Fail);           // budget+1
            Assert.AreEqual(1, item.Token.Value);
            Assert.AreEqual(1, item.Budget.Level);
            Assert.AreEqual(EvalOutcome.Fail, item.LastEval);
        }

        [TestMethod]
        public async Task AsILoopState_RoundLifecycleFullFlow()
        {
            var item = new AgentTaskItem { UserInput = "task", Scope = Scope.LoopBody, Status = TaskExecutionStatus.Running };
            item.Token.Max = 3;

            var round = await RoundLifecycle.RunAsync(
                item,
                (s, ct) => Task.FromResult(new AxiomActionResult { Ok = true, Text = "done" }),
                evaluate: null,
                CancellationToken.None);

            Assert.AreEqual(LoopDecision.Success, round.Decision);
            Assert.AreEqual(1, item.Round);
            Assert.AreEqual(EvalOutcome.Pass, item.LastEval);
        }
    }

    [TestClass]
    [TestCategory("Persistence")]
    public class AgentTaskItemJsonConverterTests
    {
        [TestMethod]
        public void RoundTrip_ExecutorId与AnswerSpec_应往返()
        {
            var item = new AgentTaskItem
            {
                TaskKey = "k1",
                UserInput = "Q",
                SchedulingMode = SchedulingMode.Deterministic,
                ExecutorId = "stateless-answer",
                AnswerSpec = new AnswerTaskSpec
                {
                    MaxRounds = 4,
                    SystemPrompt = "SYS",
                    RefineInstruction = "精化",
                    StopRuleSetKey = "stop_rule_quality",
                    RoundPromptPlan = new RoundPromptPlan
                    {
                        SystemPrompts = new Dictionary<int, string> { [1] = "S1", [2] = "S2" },
                        UserPrompts = new Dictionary<int, string> { [2] = "U2:{round}" },
                        UserTemplate = "T:{question}",
                    },
                },
            };

            var json = JsonSerializer.Serialize(item);
            var restored = JsonSerializer.Deserialize<AgentTaskItem>(json);

            Assert.IsNotNull(restored);
            Assert.AreEqual("stateless-answer", restored!.ExecutorId, "ExecutorId 应往返");
            Assert.AreEqual(4, restored.AnswerSpec!.MaxRounds, "MaxRounds 应往返");
            Assert.AreEqual("SYS", restored.AnswerSpec.SystemPrompt);
            Assert.AreEqual("精化", restored.AnswerSpec.RefineInstruction);
            Assert.AreEqual("T:{question}", restored.AnswerSpec.RoundPromptPlan!.UserTemplate, "逐轮计划应往返");
            Assert.AreEqual("S2", restored.AnswerSpec.RoundPromptPlan.SystemPrompts[2]);
            Assert.AreEqual("U2:{round}", restored.AnswerSpec.RoundPromptPlan.UserPrompts[2]);
        }

        [TestMethod]
        public void RoundTrip_无AnswerSpec_应为null()
        {
            var item = new AgentTaskItem { TaskKey = "k", UserInput = "x", SchedulingMode = SchedulingMode.Config };
            var restored = JsonSerializer.Deserialize<AgentTaskItem>(JsonSerializer.Serialize(item));
            Assert.IsNull(restored!.AnswerSpec, "未配置 AnswerSpec 应保持 null");
            Assert.IsNull(restored.ExecutorId);
        }

        [TestMethod]
        public void RoundTrip_RelatedNodes_应往返()
        {
            var item = new AgentTaskItem
            {
                TaskKey = "k2",
                UserInput = "Q",
                SchedulingMode = SchedulingMode.Deterministic,
                ExecutorId = "stateless-answer",
                RelatedNodes = new[] { "node_a", "node_b" },
            };

            var json = JsonSerializer.Serialize(item);
            var restored = JsonSerializer.Deserialize<AgentTaskItem>(json);

            Assert.IsNotNull(restored);
            CollectionAssert.AreEqual(new[] { "node_a", "node_b" }, restored!.RelatedNodes.ToArray(), "relatedNodes 应随快照往返");
        }

        [TestMethod]
        public void RoundTrip_无RelatedNodes_应为空集()
        {
            var item = new AgentTaskItem { TaskKey = "k", UserInput = "x", SchedulingMode = SchedulingMode.Config };
            var restored = JsonSerializer.Deserialize<AgentTaskItem>(JsonSerializer.Serialize(item));
            Assert.IsNotNull(restored);
            Assert.AreEqual(0, restored!.RelatedNodes.Count, "未配置 relatedNodes 应保持空集（向后兼容）");
        }
    }

}
