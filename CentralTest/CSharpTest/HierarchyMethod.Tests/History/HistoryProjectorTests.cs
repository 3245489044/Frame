using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.HierarchyMethod.History;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Execution;
using Operation.HierarchyMethod.Tasks;

namespace HierarchyMethod.Tests.History
{
    /// <summary>历史投影器验证（History 层阶段四）：把一轮编排结果沉淀为 history_owns_task / session_owns / round_sequence 边，
    /// 并把累积历史回喂为下轮上下文。写边原语委托 <see cref="IHistoryRelationManager"/>（不复制写边）；未装配守卫走 no-op 路径。</summary>
    [TestClass]
    public class HistoryProjectorTests
    {
        [TestMethod]
        public async Task PersistRound_BuildsEdges_WithHistoryId()
        {
            var relations = new FakeHistoryRelations();
            var projector = new HistoryProjector(relations);
            var round = new NodeRoundResult(new[]
            {
                new NodeExecutionRecord("collect", "wf", new Dictionary<string, object?> { ["collect_result"] = "c" }, new Dictionary<string, (string Node, string Port)>()),
                new NodeExecutionRecord("analyze", "wf", new Dictionary<string, object?> { ["analyze_result"] = "a" }, new Dictionary<string, (string Node, string Port)>()),
            });
            var scope = new TaskScope("t-1", "s-1", 2, new Dictionary<string, object?>(), "h-9");

            await projector.PersistRoundAsync(round, scope, CancellationToken.None);

            Assert.AreEqual("h-9", relations.CreatedHistoryId);
            CollectionAssert.Contains(relations.AttachedTasks, "t-1");
            CollectionAssert.AreEquivalent(new[] { "collect", "analyze" }, relations.AttachedNodes.ToArray());
            CollectionAssert.AreEquivalent(new[] { ("collect", "analyze") }, relations.Sequences.ToArray());
        }

        [TestMethod]
        public async Task PersistRound_FallsBackToTaskKeyHistory_WhenHistoryIdNull()
        {
            var relations = new FakeHistoryRelations();
            var projector = new HistoryProjector(relations);
            var round = new NodeRoundResult(new[] { new NodeExecutionRecord("node-a", "wf", new Dictionary<string, object?>(), new Dictionary<string, (string Node, string Port)>()) });
            var scope = new TaskScope("t-key", "s-1", 1, new Dictionary<string, object?>(), HistoryId: null);

            await projector.PersistRoundAsync(round, scope, CancellationToken.None);

            Assert.AreEqual("t-key", relations.CreatedHistoryId);
            CollectionAssert.Contains(relations.AttachedNodes, "node-a");
        }

        [TestMethod]
        public async Task BuildContext_RehydratesNodeRoundInput_WithHistoryTaskKeys()
        {
            var relations = new FakeHistoryRelations();
            relations.TaskKeys.Add("h-1", new List<string> { "t-1", "t-2" });
            var projector = new HistoryProjector(relations);

            var input = await projector.BuildContextAsync("h-1", "s-1", CancellationToken.None);

            Assert.AreEqual("h-1", input.TaskKey);
            Assert.AreEqual("s-1", input.SessionId);
            Assert.IsTrue(input.Vars.ContainsKey("history_task_keys"));
        }

        [TestMethod]
        public async Task UnassembledProjector_IsNoop_NotThrows()
        {
            var projector = new HistoryProjector(relations: null);
            var round = new NodeRoundResult(new[] { new NodeExecutionRecord("n", "wf", new Dictionary<string, object?>(), new Dictionary<string, (string Node, string Port)>()) });
            var scope = new TaskScope("t", "s", 1, new Dictionary<string, object?>());

            await projector.PersistRoundAsync(round, scope, CancellationToken.None); // 未装配 → 跳过，不抛
            var input = await projector.BuildContextAsync("t", "s", CancellationToken.None);

            Assert.AreEqual("t", input.TaskKey);
            Assert.AreEqual(0, input.Vars.Count);
        }

        /// <summary>轻量 IHistoryRelationManager 假实现：捕获沉淀调用，供断言边写（不落真实图）。</summary>
        private sealed class FakeHistoryRelations : IHistoryRelationManager
        {
            public string? CreatedHistoryId { get; private set; }
            public List<string> AttachedTasks { get; } = new();
            public List<string> AttachedNodes { get; } = new();
            public List<(string From, string To)> Sequences { get; } = new();
            public Dictionary<string, List<string>> TaskKeys { get; } = new();

            public Task<HistoryRecord> CreateAsync(string historyId, string? name = null, CancellationToken ct = default)
            {
                CreatedHistoryId = historyId;
                return Task.FromResult(new HistoryRecord(historyId, name ?? historyId, TaskKeys.TryGetValue(historyId, out var k) ? k : Array.Empty<string>()));
            }

            public Task<bool> DeleteAsync(string historyId, CancellationToken ct = default) => Task.FromResult(true);

            public Task AttachTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
            {
                AttachedTasks.Add(taskKey);
                return Task.CompletedTask;
            }

            public Task DetachTaskAsync(string historyId, string taskKey, CancellationToken ct = default) => Task.CompletedTask;

            public Task AttachNodeAsync(string historyId, string workflowId, string nodeKey, CancellationToken ct = default)
            {
                AttachedNodes.Add(nodeKey); // 向后兼容直挂（已降级）；历史节点集现经 LinkNodeAsync 关联。
                return Task.CompletedTask;
            }

            public Task LinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
            {
                AttachedNodes.Add(nodeKey); // task→node M:N 关联（HistoryProjector 现行写路径）。
                return Task.CompletedTask;
            }

            public Task UnlinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default) => Task.CompletedTask;

            public Task AddSequenceAsync(string workflowId, string fromNode, string toNode, CancellationToken ct = default)
            {
                Sequences.Add((fromNode, toNode));
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<string>> GetTaskKeysAsync(string historyId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<string>>(TaskKeys.TryGetValue(historyId, out var k) ? k : new List<string>());

            public Task<IReadOnlyList<HistoryTaskLink>> GetTasksAsync(string historyId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<HistoryTaskLink>>(
                    (TaskKeys.TryGetValue(historyId, out var k) ? k : new List<string>()).Select(t => new HistoryTaskLink(historyId, t)).ToList());

            public Task<IReadOnlyList<string>> GetNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

            public Task<IReadOnlyList<string>> ListHistoryIdsAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<string>>(new List<string>());
        }
    }
}

