using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.HierarchyMethod.History;
using Operation.HierarchyMethod.Reference;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace HierarchyMethod.Tests.Reference
{
    /// <summary>
    /// 通用执行标识符框架验证（P1 词汇+存取 / P2 沿边解析传导 / P3 反应式生命周期 / P4 层级批量）。
    /// 用内存 fake 存储与 fake 历史关系验证编排；写边经 fake 记录，只验证编排不复制写边（SingleImplementation / TH-12）。
    /// </summary>
    [TestClass]
    public class ExecutionFlagServiceTests
    {
        private sealed class FakeStore : IResourceReferenceStore
        {
            private readonly Dictionary<string, Resource> _res = new();
            private readonly List<(ResourceIdentifier S, ResourceIdentifier T, string Type)> _refs = new();

            public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

            public Task AddResourceAsync(ResourceIdentifier resourceId, IReadOnlyDictionary<string, object>? metadata = null, string? symbol = null, CancellationToken ct = default)
                => UpdateResourceAsync(resourceId, metadata, symbol, ct);

            public Task UpdateResourceAsync(ResourceIdentifier resourceId, IReadOnlyDictionary<string, object>? metadata = null, string? symbol = null, CancellationToken ct = default)
            {
                var key = resourceId.GetUniqueKey();
                if (_res.TryGetValue(key, out var existing))
                {
                    if (metadata is not null) foreach (var kv in metadata) existing.Metadata[kv.Key] = kv.Value;
                    if (symbol is not null) existing.Symbol = symbol;
                    existing.LastModified = DateTime.UtcNow;
                }
                else
                {
                    var r = new Resource { Identifier = resourceId, Symbol = symbol };
                    r.Metadata = metadata is null ? new Dictionary<string, object>() : new Dictionary<string, object>(metadata);
                    r.CreatedAt = r.LastModified = DateTime.UtcNow;
                    _res[key] = r;
                }
                return Task.CompletedTask;
            }

            public Task<Resource?> GetResourceAsync(ResourceIdentifier resourceId, CancellationToken ct = default)
            {
                _res.TryGetValue(resourceId.GetUniqueKey(), out var r);
                return Task.FromResult(r);
            }

            public Task<IReadOnlyList<Resource>> GetAllResourcesAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<Resource>>(_res.Values.ToList());

            public Task RemoveResourceAsync(ResourceIdentifier resourceId, CancellationToken ct = default)
            {
                _res.Remove(resourceId.GetUniqueKey());
                return Task.CompletedTask;
            }

            public Task AddReferenceAsync(ResourceIdentifier sourceId, ResourceIdentifier targetId, string refType, double strength = 1.0, CancellationToken ct = default)
            {
                _refs.Add((sourceId, targetId, refType));
                return Task.CompletedTask;
            }

            public Task RemoveReferenceAsync(ResourceIdentifier sourceId, ResourceIdentifier targetId, string refType, CancellationToken ct = default)
            {
                _refs.RemoveAll(r => r.S.GetUniqueKey() == sourceId.GetUniqueKey()
                    && r.T.GetUniqueKey() == targetId.GetUniqueKey() && r.Type == refType);
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<ResourceReference>> GetReferencesAsync(ResourceIdentifier resourceId, bool includeIncoming = true, CancellationToken ct = default)
            {
                var key = resourceId.GetUniqueKey();
                var list = new List<ResourceReference>();
                foreach (var r in _refs)
                {
                    if (r.S.GetUniqueKey() == key)
                        list.Add(new ResourceReference { Source = r.S, Target = r.T, RefType = r.Type, Strength = 1.0, IsIncoming = false });
                    else if (includeIncoming && r.T.GetUniqueKey() == key)
                        list.Add(new ResourceReference { Source = r.S, Target = r.T, RefType = r.Type, Strength = 1.0, IsIncoming = true });
                }
                return Task.FromResult<IReadOnlyList<ResourceReference>>(list);
            }
        }

        private sealed class FakeRelations : IHistoryRelationManager
        {
            public List<string> TaskKeys { get; } = new();

            public Task<HistoryRecord> CreateAsync(string historyId, string? name = null, CancellationToken ct = default)
                => Task.FromResult(new HistoryRecord(historyId, name ?? historyId, Array.Empty<string>()));
            public Task<bool> DeleteAsync(string historyId, CancellationToken ct = default) => Task.FromResult(true);
            public Task AttachTaskAsync(string historyId, string taskKey, CancellationToken ct = default) => Task.CompletedTask;
            public Task DetachTaskAsync(string historyId, string taskKey, CancellationToken ct = default) => Task.CompletedTask;
            public Task AttachNodeAsync(string historyId, string workflowId, string nodeKey, CancellationToken ct = default) => Task.CompletedTask;
            public Task LinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default) => Task.CompletedTask;
            public Task UnlinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default) => Task.CompletedTask;
            public Task AddSequenceAsync(string workflowId, string fromNode, string toNode, CancellationToken ct = default) => Task.CompletedTask;
            public Task<IReadOnlyList<string>> GetTaskKeysAsync(string historyId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<string>>(TaskKeys);
            public Task<IReadOnlyList<HistoryTaskLink>> GetTasksAsync(string historyId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<HistoryTaskLink>>(Array.Empty<HistoryTaskLink>());
            public Task<IReadOnlyList<string>> GetNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            public Task<IReadOnlyList<string>> ListHistoryIdsAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        // ---- P1：词汇 + 存取 ----

        [TestMethod]
        public void SetFlag_GetFlag_Roundtrip()
        {
            var store = new FakeStore();
            var svc = new ExecutionFlagService(store);
            var task = new EntityRef(EntityKind.Task, "T1");

            svc.SetFlagAsync(task, ExecutionFlag.Llm, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ExecutionFlag.Llm, svc.GetFlag(task));
        }

        [TestMethod]
        public void UnassembledService_NoOps_ReturnsNull()
        {
            var svc = new ExecutionFlagService(store: null);

            Assert.IsFalse(svc.IsAttached);
            Assert.IsNull(svc.GetFlag(new EntityRef(EntityKind.Task, "T1")));
            Assert.AreEqual(ExecutionFlag.Deterministic,
                svc.ResolveFlagAsync(new EntityRef(EntityKind.Task, "T1"), CancellationToken.None).GetAwaiter().GetResult());
        }

        // ---- P2：沿引用边解析传导 ----

        private static async Task SetupInheritanceChainAsync(FakeStore store)
        {
            var history = new EntityRef(EntityKind.History, "H1");
            var task = new EntityRef(EntityKind.Task, "T1");
            var node = new EntityRef(EntityKind.Node, "N1", "wf");
            await store.UpdateResourceAsync(history.ToResourceIdentifier(), new Dictionary<string, object> { ["exec.flag"] = "Llm" });
            await store.AddReferenceAsync(history.ToResourceIdentifier(), task.ToResourceIdentifier(), "history_owns_task");
            await store.AddReferenceAsync(task.ToResourceIdentifier(), node.ToResourceIdentifier(), "cross_db_link");
        }

        [TestMethod]
        public void Resolve_NodeInheritsHistory_ThreeLevel()
        {
            var store = new FakeStore();
            SetupInheritanceChainAsync(store).GetAwaiter().GetResult();
            var svc = new ExecutionFlagService(store);

            var node = new EntityRef(EntityKind.Node, "N1", "wf");
            var task = new EntityRef(EntityKind.Task, "T1");
            var history = new EntityRef(EntityKind.History, "H1");

            Assert.AreEqual(ExecutionFlag.Llm, svc.ResolveFlagAsync(node, CancellationToken.None).GetAwaiter().GetResult());
            Assert.AreEqual(ExecutionFlag.Llm, svc.ResolveFlagAsync(task, CancellationToken.None).GetAwaiter().GetResult());
            Assert.AreEqual(ExecutionFlag.Llm, svc.ResolveFlagAsync(history, CancellationToken.None).GetAwaiter().GetResult());
        }

        [TestMethod]
        public void Resolve_ExplicitFlag_OverridesParent()
        {
            var store = new FakeStore();
            SetupInheritanceChainAsync(store).GetAwaiter().GetResult();
            var node = new EntityRef(EntityKind.Node, "N1", "wf");
            store.UpdateResourceAsync(node.ToResourceIdentifier(), new Dictionary<string, object> { ["exec.flag"] = "Rule" }).GetAwaiter().GetResult();
            var svc = new ExecutionFlagService(store);

            Assert.AreEqual(ExecutionFlag.Rule, svc.ResolveFlagAsync(node, CancellationToken.None).GetAwaiter().GetResult());
        }

        [TestMethod]
        public void Resolve_AllAuto_ReturnsDeterministic()
        {
            var store = new FakeStore();
            var svc = new ExecutionFlagService(store);

            Assert.AreEqual(ExecutionFlag.Deterministic,
                svc.ResolveFlagAsync(new EntityRef(EntityKind.Node, "N1", "wf"), CancellationToken.None).GetAwaiter().GetResult());
        }


        // ---- P3：反应式生命周期 ----

        [TestMethod]
        public void Lifecycle_Transition_ValidStates()
        {
            Assert.AreEqual(FlagLifecycleState.Flagged, ExecutionFlagLifecycle.Transition(FlagLifecycleState.Idle, FlagLifecycleTrigger.SetFlag));
            Assert.AreEqual(FlagLifecycleState.Routed, ExecutionFlagLifecycle.Transition(FlagLifecycleState.Flagged, FlagLifecycleTrigger.Resolve));
            Assert.AreEqual(FlagLifecycleState.Running, ExecutionFlagLifecycle.Transition(FlagLifecycleState.Routed, FlagLifecycleTrigger.Run));
            Assert.AreEqual(FlagLifecycleState.Completed, ExecutionFlagLifecycle.Transition(FlagLifecycleState.Running, FlagLifecycleTrigger.Complete));
            Assert.AreEqual(FlagLifecycleState.Idle, ExecutionFlagLifecycle.Transition(FlagLifecycleState.Completed, FlagLifecycleTrigger.Reset));
            Assert.IsNull(ExecutionFlagLifecycle.Transition(FlagLifecycleState.Idle, FlagLifecycleTrigger.Reset)); // 非法跃迁
        }

        [TestMethod]
        public void Conductor_ProcessAsync_RoutesLlmAndResetsToStd()
        {
            var store = new FakeStore();
            var task = new EntityRef(EntityKind.Task, "T1");
            store.UpdateResourceAsync(task.ToResourceIdentifier(), new Dictionary<string, object> { ["exec.flag"] = "Llm", ["exec.lifecycle"] = "flagged" }).GetAwaiter().GetResult();
            var svc = new ExecutionFlagService(store);
            var runs = new List<ExecutionFlag>();
            svc.Conductor.SetRunPort((e, f, ct) => { runs.Add(f); return Task.CompletedTask; });

            svc.Conductor.ProcessAsync(task, CancellationToken.None).GetAwaiter().GetResult();

            // Llm → 路由到 LlmDriven → runPort 收到 Llm → 回切 Std
            Assert.AreEqual(1, runs.Count);
            Assert.AreEqual(ExecutionFlag.Llm, runs[0]);
            Assert.AreEqual(ExecutionFlag.Std, svc.GetFlag(task));
        }

        [TestMethod]
        public void Conductor_ProcessAsync_UnwiredRunPort_StillResetsToStd()
        {
            var store = new FakeStore();
            var task = new EntityRef(EntityKind.Task, "T1");
            store.UpdateResourceAsync(task.ToResourceIdentifier(), new Dictionary<string, object> { ["exec.flag"] = "Rule", ["exec.lifecycle"] = "flagged" }).GetAwaiter().GetResult();
            var svc = new ExecutionFlagService(store);

            svc.Conductor.ProcessAsync(task, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ExecutionFlag.Std, svc.GetFlag(task));
        }

        // ---- P4：层级批量 ----

        [TestMethod]
        public void SetFlagScope_TaskAllNodes_PersistsFlag()
        {
            var store = new FakeStore();
            var task1 = new EntityRef(EntityKind.Task, "T1");
            var node1 = new EntityRef(EntityKind.Node, "N1", "wf");
            var node2 = new EntityRef(EntityKind.Node, "N2", "wf");
            store.AddReferenceAsync(task1.ToResourceIdentifier(), node1.ToResourceIdentifier(), "cross_db_link").GetAwaiter().GetResult();
            store.AddReferenceAsync(task1.ToResourceIdentifier(), node2.ToResourceIdentifier(), "cross_db_link").GetAwaiter().GetResult();
            var svc = new ExecutionFlagService(store);

            svc.SetFlagScopeAsync(FlagScope.TaskAllNodes("T1"), ExecutionFlag.Rule, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ExecutionFlag.Rule, svc.GetFlag(node1));
            Assert.AreEqual(ExecutionFlag.Rule, svc.GetFlag(node2));
        }

        [TestMethod]
        public void SetFlagScope_HistoryAllTasks_WritesTasksAndNodes()
        {
            var store = new FakeStore();
            var relations = new FakeRelations { TaskKeys = { "T1", "T2" } };
            var t1 = new EntityRef(EntityKind.Task, "T1");
            var t2 = new EntityRef(EntityKind.Task, "T2");
            var nodeA = new EntityRef(EntityKind.Node, "NA", "wf");
            store.AddReferenceAsync(t1.ToResourceIdentifier(), nodeA.ToResourceIdentifier(), "cross_db_link").GetAwaiter().GetResult();
            var svc = new ExecutionFlagService(store, relations);

            svc.SetFlagScopeAsync(FlagScope.HistoryAllTasks("H1"), ExecutionFlag.Llm, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ExecutionFlag.Llm, svc.GetFlag(t1));
            Assert.AreEqual(ExecutionFlag.Llm, svc.GetFlag(t2));
            Assert.AreEqual(ExecutionFlag.Llm, svc.GetFlag(nodeA));
        }

        [TestMethod]
        public void SetFlagScope_TaskSelectedNodes_OnlyTargets()
        {
            var store = new FakeStore();
            var task1 = new EntityRef(EntityKind.Task, "T1");
            var node1 = new EntityRef(EntityKind.Node, "N1", "wf");
            var node2 = new EntityRef(EntityKind.Node, "N2", "wf");
            store.AddReferenceAsync(task1.ToResourceIdentifier(), node1.ToResourceIdentifier(), "cross_db_link").GetAwaiter().GetResult();
            store.AddReferenceAsync(task1.ToResourceIdentifier(), node2.ToResourceIdentifier(), "cross_db_link").GetAwaiter().GetResult();
            var svc = new ExecutionFlagService(store);

            svc.SetFlagScopeAsync(FlagScope.TaskSelectedNodes("T1", "wf", new[] { "N1" }), ExecutionFlag.Deterministic, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ExecutionFlag.Deterministic, svc.GetFlag(node1));
            Assert.IsNull(svc.GetFlag(node2)); // 未选 node 不受影响
        }
    }
}

