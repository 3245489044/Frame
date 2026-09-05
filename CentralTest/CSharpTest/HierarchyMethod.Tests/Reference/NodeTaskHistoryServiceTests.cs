using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.HierarchyMethod.History;
using Operation.HierarchyMethod.Reference;

namespace HierarchyMethod.Tests.Reference
{
    /// <summary>
    /// nodes/task/history 关系编排门面（NodeTaskHistoryService）验证：
    /// 统一 Id 规则（historyId == taskKey）、编排委托（Create→Attach / LinkNode / AddSequence）、可空守卫 no-op。
    /// 只验证编排，写边由 fake 记录（不复制写边逻辑，SingleImplementation / TH-12）。
    /// </summary>
    [TestClass]
    public class NodeTaskHistoryServiceTests
    {
        private sealed class FakeRelations : IHistoryRelationManager
        {
            public readonly List<(string HistoryId, string? Name)> Created = new();
            public readonly List<(string HistoryId, string TaskKey)> Attached = new();
            public readonly List<(string HistoryId, string TaskKey)> Detached = new();
            public readonly List<(string TaskKey, string Wf, string Node)> Linked = new();
            public readonly List<(string Wf, string From, string To)> Sequences = new();

            public Task<HistoryRecord> CreateAsync(string historyId, string? name = null, CancellationToken ct = default)
            {
                Created.Add((historyId, name));
                return Task.FromResult(new HistoryRecord(historyId, name ?? historyId, Array.Empty<string>()));
            }

            public Task<bool> DeleteAsync(string historyId, CancellationToken ct = default) => Task.FromResult(true);

            public Task AttachTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
            {
                Attached.Add((historyId, taskKey));
                return Task.CompletedTask;
            }

            public Task DetachTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
            {
                Detached.Add((historyId, taskKey));
                return Task.CompletedTask;
            }

            public Task AttachNodeAsync(string historyId, string workflowId, string nodeKey, CancellationToken ct = default)
                => Task.CompletedTask;

            public Task LinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
            {
                Linked.Add((taskKey, workflowId, nodeKey));
                return Task.CompletedTask;
            }

            public Task UnlinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
                => Task.CompletedTask;

            public Task AddSequenceAsync(string workflowId, string fromNode, string toNode, CancellationToken ct = default)
            {
                Sequences.Add((workflowId, fromNode, toNode));
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<string>> GetTaskKeysAsync(string historyId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

            public Task<IReadOnlyList<HistoryTaskLink>> GetTasksAsync(string historyId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<HistoryTaskLink>>(Array.Empty<HistoryTaskLink>());

            public Task<IReadOnlyList<string>> GetNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

            public Task<IReadOnlyList<string>> ListHistoryIdsAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        [TestMethod]
        public void EnsureTaskHistoryBound_UsesTaskKeyAsHistoryId()
        {
            var rel = new FakeRelations();
            var svc = new NodeTaskHistoryService(rel);

            svc.EnsureTaskHistoryBoundAsync("task-A", CancellationToken.None).GetAwaiter().GetResult();

            // 统一 Id 规则：historyId == taskKey（消除旧 UI 层 default 与 pipeline 层 sessionName 的二义）
            Assert.AreEqual(1, rel.Created.Count);
            Assert.AreEqual("task-A", rel.Created[0].HistoryId);
            Assert.AreEqual("task-A", rel.Created[0].Name);
            Assert.AreEqual(1, rel.Attached.Count);
            Assert.AreEqual(("task-A", "task-A"), rel.Attached[0]);
        }

        [TestMethod]
        public void EnsureHistory_DelegatesToCreateWithName()
        {
            var rel = new FakeRelations();
            var svc = new NodeTaskHistoryService(rel);

            svc.EnsureHistoryAsync("history:analysis", "分析历史", CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(1, rel.Created.Count);
            Assert.AreEqual("history:analysis", rel.Created[0].HistoryId);
            Assert.AreEqual("分析历史", rel.Created[0].Name);
            Assert.AreEqual(0, rel.Attached.Count); // 纯建历史，不归拢 task
        }

        [TestMethod]
        public void AttachTaskToHistory_DelegatesToExplicitHistory()
        {
            var rel = new FakeRelations();
            var svc = new NodeTaskHistoryService(rel);

            svc.AttachTaskToHistoryAsync("history:analysis", "task-X", CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(1, rel.Attached.Count);
            Assert.AreEqual(("history:analysis", "task-X"), rel.Attached[0]);
        }

        [TestMethod]
        public void DetachTaskFromHistory_UsesTaskKey()
        {
            var rel = new FakeRelations();
            var svc = new NodeTaskHistoryService(rel);

            svc.DetachTaskFromHistoryAsync("task-B", "task-B", CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(1, rel.Detached.Count);
            Assert.AreEqual(("task-B", "task-B"), rel.Detached[0]);
        }

        [TestMethod]
        public void AttachNodeToTask_DelegatesToLinkNode()
        {
            var rel = new FakeRelations();
            var svc = new NodeTaskHistoryService(rel);

            svc.AttachNodeToTaskAsync("task-C", "wf-1", "node-1", CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(1, rel.Linked.Count);
            Assert.AreEqual(("task-C", "wf-1", "node-1"), rel.Linked[0]);
        }

        [TestMethod]
        public void AddSequence_DelegatesToSequenceEdge()
        {
            var rel = new FakeRelations();
            var svc = new NodeTaskHistoryService(rel);

            svc.AddSequenceAsync("wf-1", "a", "b", CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(1, rel.Sequences.Count);
            Assert.AreEqual(("wf-1", "a", "b"), rel.Sequences[0]);
        }

        [TestMethod]
        public void UnassembledService_NoOps_NotThrows()
        {
            var svc = new NodeTaskHistoryService(relations: null);

            Assert.IsFalse(svc.IsAttached);
            svc.EnsureTaskHistoryBoundAsync("task-A", CancellationToken.None).GetAwaiter().GetResult();
            svc.DetachTaskFromHistoryAsync("task-A", "task-A", CancellationToken.None).GetAwaiter().GetResult();
            svc.AttachNodeToTaskAsync("task-A", "wf-1", "n-1", CancellationToken.None).GetAwaiter().GetResult();
            svc.AddSequenceAsync("wf-1", "a", "b", CancellationToken.None).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void BlankTaskKey_NoOps()
        {
            var rel = new FakeRelations();
            var svc = new NodeTaskHistoryService(rel);

            svc.EnsureTaskHistoryBoundAsync("  ", CancellationToken.None).GetAwaiter().GetResult();
            svc.DetachTaskFromHistoryAsync(null!, null!, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(0, rel.Created.Count);
            Assert.AreEqual(0, rel.Detached.Count);
        }
    }
}

