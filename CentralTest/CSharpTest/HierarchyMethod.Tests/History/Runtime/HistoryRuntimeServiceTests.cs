using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.HierarchyMethod.History;
using Operation.HierarchyMethod.History.Runtime;

namespace HierarchyMethod.Tests.History.Runtime
{
    /// <summary>
    /// history 会话编排门面验证（运行态聚合 / 批量控制 / 装配声明）——用内存 fake 关系端口 + fake 运行端口验证编排，
    /// 只验证编排不复制写边 / 控制原语（SingleImplementation / TH-12）。
    /// </summary>
    [TestClass]
    public class HistoryRuntimeServiceTests
    {
        private sealed class FakeRelations : IHistoryRelationManager
        {
            public IReadOnlyList<string> Owned { get; set; } = Array.Empty<string>();

            public Task<HistoryRecord> CreateAsync(string historyId, string? name = null, CancellationToken ct = default)
                => Task.FromResult(new HistoryRecord(historyId, name ?? historyId));
            public Task<bool> DeleteAsync(string historyId, CancellationToken ct = default) => Task.FromResult(true);
            public Task AttachTaskAsync(string historyId, string taskKey, CancellationToken ct = default) => Task.CompletedTask;
            public Task DetachTaskAsync(string historyId, string taskKey, CancellationToken ct = default) => Task.CompletedTask;
            public Task AttachNodeAsync(string historyId, string workflowId, string nodeKey, CancellationToken ct = default) => Task.CompletedTask;
            public Task LinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default) => Task.CompletedTask;
            public Task UnlinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default) => Task.CompletedTask;
            public Task AddSequenceAsync(string workflowId, string fromNode, string toNode, CancellationToken ct = default) => Task.CompletedTask;
            public Task<IReadOnlyList<string>> GetTaskKeysAsync(string historyId, CancellationToken ct = default)
                => Task.FromResult(Owned);
            public Task<IReadOnlyList<HistoryTaskLink>> GetTasksAsync(string historyId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<HistoryTaskLink>>(Array.Empty<HistoryTaskLink>());
            public Task<IReadOnlyList<string>> GetNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            public Task<IReadOnlyList<string>> ListHistoryIdsAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        private sealed class FakePort : IHistoryRuntimePort
        {
            public Dictionary<string, HistoryTaskRunStatus> Statuses { get; } = new();
            public List<string> Started { get; } = new();
            public List<string> Paused { get; } = new();
            public List<string> Resumed { get; } = new();
            public List<string> Cancelled { get; } = new();
            public List<string> Stepped { get; } = new();

            public Task<HistoryTaskRunStatus?> GetStatusAsync(string taskKey, CancellationToken ct = default)
            {
                Statuses.TryGetValue(taskKey, out var s);
                return Task.FromResult(s);
            }
            public Task<bool> StartAsync(string taskKey, HistoryPromptProfile? profile, string? input, CancellationToken ct = default)
            { Started.Add(taskKey); return Task.FromResult(true); }
            public Task<bool> PauseAsync(string taskKey, CancellationToken ct = default)
            { Paused.Add(taskKey); return Task.FromResult(true); }
            public Task<bool> ResumeAsync(string taskKey, CancellationToken ct = default)
            { Resumed.Add(taskKey); return Task.FromResult(true); }
            public Task<bool> CancelAsync(string taskKey, CancellationToken ct = default)
            { Cancelled.Add(taskKey); return Task.FromResult(true); }
            public Task<bool> StepAsync(string taskKey, string? input, CancellationToken ct = default)
            { Stepped.Add(taskKey); return Task.FromResult(true); }
        }

        [TestMethod]
        public async Task Overview_AggregatesOwnedTaskStatuses()
        {
            var rel = new FakeRelations { Owned = new[] { "t1", "t2", "t3" } };
            var port = new FakePort();
            port.Statuses["t1"] = new HistoryTaskRunStatus("t1", "cline", "Running", "Running", 2, true);
            port.Statuses["t2"] = new HistoryTaskRunStatus("t2", "stateless-answer", "Paused", "Paused", 1, true);
            port.Statuses["t3"] = new HistoryTaskRunStatus("t3", "cline", "Completed", "Done", 0, false);

            var svc = new HistoryRuntimeService(rel, port);
            var view = await svc.GetOverviewAsync("h1");

            Assert.AreEqual(3, view.TaskCount);
            Assert.AreEqual(2, view.ActiveCount);
            Assert.AreEqual(1, view.RunningCount);
            Assert.AreEqual(1, view.PausedCount);
            Assert.AreEqual(1, view.DoneCount);
            Assert.AreEqual(0, view.ErrorCount);
            Assert.AreEqual(3, view.Tasks.Count);
        }

        [TestMethod]
        public async Task PauseSingle_TargetsOnlyOwnedTask()
        {
            var rel = new FakeRelations { Owned = new[] { "t1", "t2" } };
            var port = new FakePort();
            var svc = new HistoryRuntimeService(rel, port);

            var n = await svc.PauseAsync("h1", "t2");

            Assert.AreEqual(1, n);
            CollectionAssert.AreEquivalent(new[] { "t2" }, port.Paused);
        }

        [TestMethod]
        public async Task Step_ScopeGuard_RejectsNonOwnedTask()
        {
            var rel = new FakeRelations { Owned = new[] { "t1" } };
            var port = new FakePort();
            var svc = new HistoryRuntimeService(rel, port);

            var ok = await svc.StepAsync("h1", "foreign");

            Assert.IsFalse(ok);
            Assert.AreEqual(0, port.Stepped.Count);
        }

        [TestMethod]
        public async Task Step_OwnedTask_DelegatesToPort()
        {
            var rel = new FakeRelations { Owned = new[] { "t1" } };
            var port = new FakePort();
            var svc = new HistoryRuntimeService(rel, port);

            var ok = await svc.StepAsync("h1", "t1", "next");

            Assert.IsTrue(ok);
            CollectionAssert.AreEquivalent(new[] { "t1" }, port.Stepped);
        }

        [TestMethod]
        public async Task Profile_FallsBackToMemory()
        {
            var svc = new HistoryRuntimeService();
            var profile = new HistoryPromptProfile(WorkflowKey: "session-answer", Agent: "answer");

            await svc.SetProfileAsync("h1", profile);
            var got = await svc.GetProfileAsync("h1");

            Assert.IsNotNull(got);
            Assert.AreEqual("session-answer", got!.WorkflowKey);
            Assert.AreEqual("answer", got.Agent);
        }

        [TestMethod]
        public async Task Control_WithoutPort_ReturnsZero()
        {
            var svc = new HistoryRuntimeService(new FakeRelations { Owned = new[] { "t1" } });
            Assert.AreEqual(0, await svc.PauseAsync("h1"));
            Assert.AreEqual(0, await svc.StartAsync("h1", new HistoryPromptProfile()));
            Assert.IsFalse(await svc.StepAsync("h1", "t1"));
        }
    }
}
