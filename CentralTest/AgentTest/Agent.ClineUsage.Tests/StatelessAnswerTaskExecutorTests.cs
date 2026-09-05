using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Contract.Answer;
using Agent.Contract.Llm;
using Agent.Contract.Nodes;
using Agent.Contract.Scheduling;
using Agent.Core.TaskPool;
using Agent.Modules.StatelessAnswer;
using Operation.HierarchyMethod.Nodes;
using WorkFlowMethod.LoopFramework.Axioms;

namespace Agent.Modules.Tests
{
    /// <summary>
    /// 无状态回答的任务池执行器（<see cref="StatelessAnswerTaskExecutor"/>）测试：
    /// 验证"所有调用逻辑都经任务池"——任务经 ExecutorId 路由到本执行器，在任务池消费内运行显式循环
    /// （Deterministic + MaxRounds + 逐轮提示词计划），每轮 system/user 按计划送入 LLM。
    /// </summary>
    [TestClass]
    [TestCategory("Modules_Unit")]
    public class StatelessAnswerTaskExecutorTests
    {
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

        /// <summary>内存版节点输出读取端口（消费桥测试用）：按 workflowId+nodeKey 存读 NodeOutput。</summary>
        private sealed class FakeNodeOutputStore : INodeOutputStore
        {
            private readonly Dictionary<string, NodeOutput> _nodes = new(StringComparer.Ordinal);

            private static string Key(string workflowId, string nodeKey) => workflowId + "|" + nodeKey;

            public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

            public Task WriteOutputAsync(string workflowId, string nodeKey, string port, object? value, CancellationToken ct = default)
            {
                if (value is null) return Task.CompletedTask;
                var k = Key(workflowId, nodeKey);
                if (!_nodes.TryGetValue(k, out var node))
                {
                    node = new NodeOutput { NodeKey = nodeKey, WorkflowId = workflowId };
                    _nodes[k] = node;
                }
                node.OutputPorts[port] = value;
                return Task.CompletedTask;
            }

            public Task WriteOutputAsync(string workflowId, string nodeKey, string port, object? value, OutputVisibility visibility, CancellationToken ct = default)
                => WriteOutputAsync(workflowId, nodeKey, port, value, ct);

            public Task<NodeOutput?> TryGetNodeOutputAsync(string workflowId, string nodeKey, CancellationToken ct = default)
                => Task.FromResult(_nodes.TryGetValue(Key(workflowId, nodeKey), out var n) ? n : null);

            public Task<NodeOutput?> TryGetNodeOutputScopedAsync(string workflowId, string nodeKey, string requestingWorkflowId, CancellationToken ct = default)
                => TryGetNodeOutputAsync(workflowId, nodeKey, ct);

            public Task<IReadOnlyList<NodeOutput>> EnumerateOutputsAsync(string workflowId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<NodeOutput>>(_nodes.Values.Where(o => o.WorkflowId == workflowId).ToList());

            public Task<IReadOnlyList<NodeOutput>> EnumerateOutputsScopedAsync(string workflowId, string requestingWorkflowId, CancellationToken ct = default)
                => EnumerateOutputsAsync(workflowId, ct);

            public Task<IReadOnlyList<string>> EnumerateNodeKeysAsync(string workflowId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<string>>(_nodes.Values.Where(o => o.WorkflowId == workflowId).Select(o => o.NodeKey).ToList());

            public Task<IReadOnlyList<global::Operation.HierarchyMethod.Nodes.NodeSignal>> EnumerateSignalsAsync(string workflowId, string? module = null, string? nodeType = null, string? region = null, int limit = -1, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<global::Operation.HierarchyMethod.Nodes.NodeSignal>>(_nodes.Values.Where(o => o.WorkflowId == workflowId).Select(o => new global::Operation.HierarchyMethod.Nodes.NodeSignal(o.NodeKey, o.WorkflowId, o.Module, o.Region, o.NodeType, o.Freshness, o.ValueScore, o.OutputDigest, o.Sticky, o.Priority)).ToList());

            public Task WriteSignalMetadataAsync(string workflowId, string nodeKey, global::Operation.HierarchyMethod.Nodes.NodeSignal signal, CancellationToken ct = default)
                => Task.FromResult(signal);

            public Task ClearNodeAsync(string workflowId, string nodeKey, CancellationToken ct = default)
            { _nodes.Remove(Key(workflowId, nodeKey)); return Task.CompletedTask; }

            public Task ClearWorkflowAsync(string workflowId, CancellationToken ct = default)
            {
                foreach (var k in _nodes.Keys.Where(k => k.StartsWith(workflowId + "|", StringComparison.Ordinal)).ToList())
                    _nodes.Remove(k);
                return Task.CompletedTask;
            }
        }

        [TestMethod]
        public void CanExecute_ExecutorId匹配_返回true()
        {
            var ex = new StatelessAnswerTaskExecutor(new FakeLlmClient(), TimeSpan.FromSeconds(30));
            Assert.IsTrue(ex.CanExecute(new AgentTaskItem { ExecutorId = "stateless-answer" }), "ExecutorId=stateless-answer 应路由到本执行器");
            Assert.IsFalse(ex.CanExecute(new AgentTaskItem { ExecutorId = "llm" }), "其他 ExecutorId 不应路由到本执行器");
        }

        [TestMethod]
        public async Task ExecuteAsync_按逐轮计划送入system与user_预算轮数受控()
        {
            var fake = new FakeLlmClient();
            var ex = new StatelessAnswerTaskExecutor(fake, TimeSpan.FromSeconds(30));
            var item = new AgentTaskItem
            {
                UserInput = "Q",
                SchedulingMode = SchedulingMode.Deterministic,
                ExecutorId = "stateless-answer",
                AnswerSpec = new AnswerTaskSpec
                {
                    MaxRounds = 2,
                    SystemPrompt = "SYS",
                    // 纯预算：不做达标判定（None），使循环按 MaxRounds=2 耗尽（TO）而非首轮非空即接受（Success）——
                    // 否则 fake 返回非空文本会命中默认 FirstNonEmpty，第 1 轮即提前停止，无法验证预算轮数受控。
                    AcceptancePolicy = new AnswerAcceptancePolicy { Mode = AnswerAcceptMode.None },
                    RoundPromptPlan = new RoundPromptPlan
                    {
                        SystemPrompts = new Dictionary<int, string> { [1] = "S1", [2] = "S2" },
                        UserPrompts = new Dictionary<int, string> { [1] = "U1:{question}", [2] = "U2:{round}:{prior_answer}" },
                    },
                },
            };

            var result = await ex.ExecuteAsync(item, item.TaskKey, CancellationToken.None).ConfigureAwait(false);

            Assert.IsInstanceOfType(result, typeof(AxiomLoopResult));
            Assert.AreEqual(2, result.Rounds.Count, "MaxRounds=2 → 恰好两轮（预算受控）");
            Assert.AreEqual(2, fake.Calls.Count, "每轮一次 LLM 调用");
            Assert.AreEqual(("S1", "U1:Q"), (fake.Calls[0].System, fake.Calls[0].User), "第一轮用计划 system+user");
            Assert.AreEqual(("S2", "U2:2:U1:Q"), (fake.Calls[1].System, fake.Calls[1].User), "第二轮用计划 system+user（含上轮回答）");
            // 预算轮数=2 耗尽 → TO（Timeout），非 LLM 自主成功。
            Assert.AreEqual(LoopDecision.Timeout, result.FinalDecision, "预算耗尽终止（TO），非自主停止");
        }

        [TestMethod]
        public async Task ExecuteAsync_MaxRounds1_单轮即止()
        {
            var fake = new FakeLlmClient();
            var ex = new StatelessAnswerTaskExecutor(fake, TimeSpan.FromSeconds(30));
            var item = new AgentTaskItem
            {
                UserInput = "Q",
                SchedulingMode = SchedulingMode.Deterministic,
                ExecutorId = "stateless-answer",
                AnswerSpec = new AnswerTaskSpec { MaxRounds = 1 },
            };

            var result = await ex.ExecuteAsync(item, item.TaskKey, CancellationToken.None).ConfigureAwait(false);

            Assert.AreEqual(1, result.Rounds.Count, "MaxRounds=1 → 单轮即止");
            Assert.AreEqual(1, fake.Calls.Count, "单轮一次 LLM 调用");
        }

        [TestMethod]
        public async Task ExecuteAsync_RelatedNodes_装配引用节点payload进Question()
        {
            var fake = new FakeLlmClient();
            var nodes = new FakeNodeOutputStore();
            // 用 ASCII 值，避免 JSON 序列化将非 ASCII 转义为 \uXXXX 干扰断言。
            await nodes.WriteOutputAsync(NodeKeys.DefaultWorkflowId, "src", "summary", "DATA:hello", CancellationToken.None).ConfigureAwait(false);
            var ex = new StatelessAnswerTaskExecutor(fake, TimeSpan.FromSeconds(30), nodeOutputs: nodes);
            var item = new AgentTaskItem
            {
                UserInput = "请分析：",
                SchedulingMode = SchedulingMode.Deterministic,
                ExecutorId = "stateless-answer",
                RelatedNodes = new[] { "src" },
                AnswerSpec = new AnswerTaskSpec { MaxRounds = 1 },
            };

            await ex.ExecuteAsync(item, item.TaskKey, CancellationToken.None).ConfigureAwait(false);

            Assert.AreEqual(1, fake.Calls.Count, "单轮一次 LLM 调用");
            var user = fake.Calls[0].User;
            StringAssert.Contains(user, "请分析：", "Question 应保留原始 userInput");
            StringAssert.Contains(user, "参考节点 src", "应按 relatedNodes 注入引用节点块");
            StringAssert.Contains(user, "DATA:hello", "引用节点 payload 应装配进 Question");
        }

        [TestMethod]
        public async Task ExecuteAsync_缺nodeOutputs_回退原文()
        {
            var fake = new FakeLlmClient();
            // 未注入节点读取端口（nodeOutputs=null）→ 消费桥整体跳过，Question 保持原文（向后兼容）。
            var ex = new StatelessAnswerTaskExecutor(fake, TimeSpan.FromSeconds(30));
            var item = new AgentTaskItem
            {
                UserInput = "Q",
                SchedulingMode = SchedulingMode.Deterministic,
                ExecutorId = "stateless-answer",
                RelatedNodes = new[] { "src" },
                AnswerSpec = new AnswerTaskSpec { MaxRounds = 1 },
            };

            await ex.ExecuteAsync(item, item.TaskKey, CancellationToken.None).ConfigureAwait(false);

            Assert.AreEqual(1, fake.Calls.Count);
            Assert.AreEqual("Q", fake.Calls[0].User, "未装配节点读取端口时不应注入引用块，Question 保持原文");
        }

        [TestMethod]
        public async Task ExecuteAsync_RelatedNodes缺失节点_failFast抛异常()
        {
            var fake = new FakeLlmClient();
            var ex = new StatelessAnswerTaskExecutor(fake, TimeSpan.FromSeconds(30), nodeOutputs: new FakeNodeOutputStore());
            var item = new AgentTaskItem
            {
                UserInput = "Q",
                SchedulingMode = SchedulingMode.Deterministic,
                ExecutorId = "stateless-answer",
                RelatedNodes = new[] { "missing" },
                AnswerSpec = new AnswerTaskSpec { MaxRounds = 1 },
            };

            // 缺失即 fail-fast：避免下游分析拿到不完整上下文（经任务池 handler 落 Faulted）。
            await AssertThrowsAsync<InvalidOperationException>(
                () => ex.ExecuteAsync(item, item.TaskKey, CancellationToken.None)).ConfigureAwait(false);
        }

        /// <summary>MSTest 版本不含 Assert.ThrowsExceptionAsync；本地辅助（验证指定异常抛出）。</summary>
        private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (TException ex)
            {
                return ex;
            }
            throw new AssertFailedException($"应抛出 {typeof(TException).Name}，但未抛出。");
        }
    }
}
