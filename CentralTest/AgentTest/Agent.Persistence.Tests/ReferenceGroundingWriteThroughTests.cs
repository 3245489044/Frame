using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using Operation.DataStoreDriven.Graph;
using Share.TaskStatusPool.TaskPool;
using UniversalWorkflowFramework.Core.Models;
using TestInfrastructure;

namespace Agent.Persistence.Tests;

/// <summary>
/// 引用自动落地守卫（ReferenceGrounding）+ 写透（WriteGuardPort / WriteThroughTaskPoolStore）+ 边推导（NodeRelationDeriver）测试：
/// I1 写透（业务实体 Put/Delete 自动同步图镜像 + 白名单过滤）、节点关系边推导统一写入、task 镜像、删除级联清边。
/// </summary>
[TestClass]
public class ReferenceGroundingWriteThroughTests : IntegrationTestBase
{
    private const string Wf = "agent-round";
    private static string _dir = string.Empty;
    private static string _dbPath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = CreateTempDirectory();
        _dbPath = Path.Combine(_dir, "graph.db");
    }

    private static ResourceIdentifier NodeRes(string nodeKey)
        => new() { ResourceType = NodeResourceTypes.Node, Id = NodeRelationFactory.NodeId(Wf, nodeKey), Location = Wf };

    [TestMethod]
    public async Task WriteGuardPort_PutBusinessNode_CreatesMirror_And_NonBusiness_DoesNot()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var grounding = new ReferenceGrounding(graph);
        var store = new WriteGuardPort(new InMemoryNodeStore(), grounding);

        // 业务节点（NodeType=node）→ 写透自动建 wf.node 图镜像（I1）。
        await store.PutAsync(new NodeRecord { NodeKey = "cline_execute", NodeType = NodeTypes.Node, ThreadId = Wf });
        Assert.IsNotNull(await graph.GetResourceAsync(NodeRes("cline_execute")), "业务节点写透后应出现 wf.node 图镜像");

        // keymapping（NodeType=keymapping）→ 白名单仅 {node}，不建图镜像（镜像由边写入的 EnsureResourceAsync 产生）。
        await store.PutAsync(new NodeRecord { NodeKey = "keymapping.input.agent-round.cline_execute", NodeType = NodeTypes.KeyMapping });
        var kmMirrorId = NodeRelationFactory.NodeId(Wf, "keymapping.input.agent-round.cline_execute");
        Assert.IsNull(await graph.GetResourceAsync(new ResourceIdentifier { ResourceType = NodeResourceTypes.Node, Id = kmMirrorId, Location = Wf }),
            "非业务节点（keymapping）写透不应建 node 图镜像");
    }

    [TestMethod]
    public async Task WriteGuardPort_DeleteBusinessNode_RemovesMirrorCascade()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var grounding = new ReferenceGrounding(graph);
        var store = new WriteGuardPort(new InMemoryNodeStore(), grounding);

        await store.PutAsync(new NodeRecord { NodeKey = "guard_output", NodeType = NodeTypes.Node, ThreadId = Wf });
        Assert.IsNotNull(await graph.GetResourceAsync(NodeRes("guard_output")));

        var removed = await store.DeleteAsync("guard_output");
        Assert.IsTrue(removed);
        Assert.IsNull(await graph.GetResourceAsync(NodeRes("guard_output")), "删除业务节点应级联移除图镜像");
    }

    [TestMethod]
    public async Task ReferenceGrounding_WritesDerivedNodeEdges()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var grounding = new ReferenceGrounding(graph);
        INodeRelationStore graphRelations = graph;

        // 边推导（NodeRelationDeriver 唯一推导器）：KeyMappingToNode + NodeToNode（依溯源）。
        var edges = new List<NodeRelation>
        {
            NodeRelationDeriver.DeriveKeyMappingEdge(Wf, "cline_execute"),
        };
        edges.AddRange(NodeRelationDeriver.DeriveNodeToNodeEdges(Wf, "cline_execute", new Dictionary<string, (string Node, string Port)>
        {
            ["output"] = ("previous_node", "result"),
        }));
        // 引用守卫统一写边入口（对账收敛：补权威边、删陈旧边；幂等）。WriteRelationsAsync 为唯一写边入口。
        await grounding.WriteRelationsAsync(Wf, "cline_execute", edges);

        // KeyMapping↔节点 边。
        var kmEdges = await graphRelations.GetRelationsAsync(Wf, "cline_execute", NodeRelationType.KeyMappingToNode);
        Assert.AreEqual(1, kmEdges.Count);
        Assert.AreEqual(NodeResourceTypes.KeyMapping, kmEdges[0].SourceResourceType);

        // 节点↔节点 数据流组合边：raw refType = node_to_node:result>output（端口组合编码，可还原 fromPort>toPort）。
        var raw = await graph.GetReferencesAsync(NodeRes("cline_execute"), includeIncoming: true);
        var n2nRaw = raw.First(r => r.RefType.StartsWith("node_to_node:"));
        Assert.AreEqual("node_to_node:result>output", n2nRaw.RefType);
        Assert.AreEqual(NodeRelationFactory.NodeId(Wf, "previous_node"), n2nRaw.Source.Id);
        Assert.AreEqual(NodeRelationFactory.NodeId(Wf, "cline_execute"), n2nRaw.Target.Id);
    }

    [TestMethod]
    public async Task WriteThroughTaskPoolStore_EnqueueDelete_MirrorsTask()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var grounding = new ReferenceGrounding(graph);
        var store = new WriteThroughTaskPoolStore<TestPoolItem>(
            new InMemoryTaskPoolPersistence<TestPoolItem>(), grounding);

        await store.EnqueueAsync(new TestPoolItem { TaskKey = "task-1" });
        Assert.IsNotNull(await graph.GetResourceAsync(new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = "task-1" }),
            "任务入队应写透建 task 图镜像");

        var removed = await store.DeleteAsync("task-1");
        Assert.IsTrue(removed);
        Assert.IsNull(await graph.GetResourceAsync(new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = "task-1" }),
            "删除任务应级联移除 task 图镜像");
    }

    [TestMethod]
    public async Task WriteGuardPort_PutWithRelations_ReconcilesAndRemovesStaleEdge()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var grounding = new ReferenceGrounding(graph);
        INodeRelationStore graphRelations = graph;
        var store = new WriteGuardPort(new InMemoryNodeStore(), grounding);

        var relationsJson = NodeRelationDeriver.SerializeRelations(
            hasMapping: true,
            new Dictionary<string, (string Node, string Port)> { ["input"] = ("upstream", "result") });

        // 首次写：有映射 + 有溯源 → KeyMappingToNode + NodeToNode 边均落地。
        await store.PutAsync(new NodeRecord { NodeKey = "node_b", NodeType = NodeTypes.Node, ThreadId = Wf, RelationsJson = relationsJson });
        Assert.AreEqual(1, (await graphRelations.GetRelationsAsync(Wf, "node_b", NodeRelationType.KeyMappingToNode)).Count);
        Assert.AreEqual(1, (await graphRelations.GetRelationsAsync(Wf, "node_b", NodeRelationType.NodeToNode)).Count);

        // 修改节点（移除溯源：该参数改由数据空间直供）→ 重推导权威边仅剩 KeyMappingToNode，NodeToNode 过期边被对账删除。
        var staleJson = NodeRelationDeriver.SerializeRelations(hasMapping: true, null);
        await store.PutAsync(new NodeRecord { NodeKey = "node_b", NodeType = NodeTypes.Node, ThreadId = Wf, RelationsJson = staleJson });
        Assert.AreEqual(0, (await graphRelations.GetRelationsAsync(Wf, "node_b", NodeRelationType.NodeToNode)).Count,
            "移除溯源后 NodeToNode 过期边应对账删除");
        Assert.AreEqual(1, (await graphRelations.GetRelationsAsync(Wf, "node_b", NodeRelationType.KeyMappingToNode)).Count);
    }

    [TestMethod]
    public async Task WriteGuardPort_PutBusinessNode_MirrorContentFollows_OnModification()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var grounding = new ReferenceGrounding(graph);
        var store = new WriteGuardPort(new InMemoryNodeStore(), grounding);

        await store.PutAsync(new NodeRecord { NodeKey = "n1", NodeType = NodeTypes.Node, ThreadId = Wf, Status = "executed", PayloadJson = "{\"v\":1}" });
        Assert.IsNotNull(await graph.GetResourceAsync(NodeRes("n1")), "首次写应建图镜像");

        // 修改节点 → 端点镜像仍存在且不重复（实体载荷/状态以 SQL ur_nodes 为准，LiteDB 只存关系、不复制内容）。
        await store.PutAsync(new NodeRecord { NodeKey = "n1", NodeType = NodeTypes.Node, ThreadId = Wf, Status = "rerun", PayloadJson = "{\"v\":2}" });
        var res2 = await graph.GetResourceAsync(NodeRes("n1"));
        Assert.IsNotNull(res2, "修改后端点镜像应存在（upsert）");
        Assert.AreEqual("n1", res2.Symbol);



        var all = await graph.GetAllResourcesAsync();
        Assert.AreEqual(1, all.Count(r => string.Equals(r.Identifier.ResourceType, NodeResourceTypes.Node, StringComparison.Ordinal)),
            "修改节点不应产生重复图端点");
    }

    [TestMethod]
    public async Task NodeGraphReconciler_ReconcileAll_RestoresMissingEdgesFromStoredRelations()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var grounding = new ReferenceGrounding(graph);
        var inner = new InMemoryNodeStore();
        var store = new WriteGuardPort(inner, grounding);
        INodeRelationStore graphRelations = graph;

        var relationsJson = NodeRelationDeriver.SerializeRelations(
            hasMapping: true,
            new Dictionary<string, (string Node, string Port)> { ["input"] = ("upstream", "result") });
        await store.PutAsync(new NodeRecord { NodeKey = "target", NodeType = NodeTypes.Node, ThreadId = Wf, RelationsJson = relationsJson });
        Assert.AreEqual(2, (await graphRelations.GetRelationsAsync(Wf, "target")).Count);

        // 人为删掉图内全部入边（模拟跨介质写丢/悬空）→ 对账器从 ur_nodes 持久化关系状态重推导恢复。
        foreach (var r in await graphRelations.GetRelationsAsync(Wf, "target"))
            await graphRelations.RemoveRelationAsync(Wf, r);
        Assert.AreEqual(0, (await graphRelations.GetRelationsAsync(Wf, "target")).Count);

        var reconciler = new NodeGraphReconciler(inner, grounding);
        var processed = await reconciler.ReconcileAllAsync();
        Assert.AreEqual(1, processed);
        Assert.AreEqual(2, (await graphRelations.GetRelationsAsync(Wf, "target")).Count,
            "全量对账应从持久化关系状态恢复全部权威边");
    }

    [TestMethod]
    public async Task WriteThroughTaskPoolStore_UpdateStatusPayload_StillMirrorsTaskEndpoint()
    {
        // B4/D3 回归：UpdateStatus/UpdatePayload（统一经 UpdateAndMirrorAsync，ct 透传 + 双重载收拢）仍应镜像 task 图端点（幂等，不重建边）。
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var grounding = new ReferenceGrounding(graph);
        var store = new WriteThroughTaskPoolStore<TestPoolItem>(
            new InMemoryTaskPoolPersistence<TestPoolItem>(), grounding);

        await store.EnqueueAsync(new TestPoolItem { TaskKey = "task-u1" });
        // 入队后状态为 NotStarted，按生命周期逐级推进（Running→Completed 前置 NotStarted→Running 才成立）。
        Assert.IsTrue(await store.UpdateStatusAsync("task-u1", TaskExecutionStatus.NotStarted, TaskExecutionStatus.Running));
        Assert.IsTrue(await store.UpdateStatusAsync("task-u1", TaskExecutionStatus.Running, TaskExecutionStatus.Completed));
        await store.UpdatePayloadAsync("task-u1", new TestPoolItem { TaskKey = "task-u1" });

        Assert.IsNotNull(await graph.GetResourceAsync(new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = "task-u1" }),
            "Update 后 task 图镜像端点仍存在（B4/D3 收拢不破坏镜像语义）");
    }

    [TestMethod]
    public async Task ReferenceGrounding_ReconcileNode_UsesNodeThreadIdAsWorkflowAuthority()
    {
        // B1 回归护栏：Node 作用域对账的 workflowId 真源应为节点自身 ThreadId（WorkflowIds.Resolve），
        // 而非调用方入参——与 All 作用域 / 写透守卫同权威，消除图镜像 id 漂移（E 类风险）。
        const string customWf = "wf-custom"; // 节点自身 ThreadId（≠ 默认/入参 Wf）
        var relationsJson = NodeRelationDeriver.SerializeRelations(
            hasMapping: true,
            new Dictionary<string, (string Node, string Port)> { ["input"] = ("upstream", "result") });

        var inner = new InMemoryNodeStore();
        await inner.PutAsync(new NodeRecord { NodeKey = "b1_node", NodeType = NodeTypes.Node, ThreadId = customWf, RelationsJson = relationsJson });

        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var grounding = new ReferenceGrounding(graph) { NodeStore = inner };

        // 传入与节点 ThreadId 不同的 workflowId（复现旧 B1 缺陷：入参代替节点权威解析）。
        var processed = await grounding.ReconcileAsync(MaintenanceScope.Node, workflowId: Wf, nodeKey: "b1_node");
        Assert.AreEqual(1, processed, "携带关系状态的单节点应被对账处理");

        // 权威边应落在节点自身 ThreadId 命名空间（customWf），而非入参 Wf。
        Assert.AreEqual(1, (await graph.GetRelationsAsync(customWf, "b1_node", NodeRelationType.KeyMappingToNode)).Count,
            "对账应使用节点自身 ThreadId 作为 workflowId 真源");
        Assert.AreEqual(0, (await graph.GetRelationsAsync(Wf, "b1_node", NodeRelationType.KeyMappingToNode)).Count,
            "不应在入参 workflowId 命名空间建边（消除 E 类 id 漂移）");
    }

    [TestMethod]
    public void NodeRelationDeriver_SerializeDeserialize_RoundTripsEdges()
    {
        var json = NodeRelationDeriver.SerializeRelations(
            hasMapping: true,
            new Dictionary<string, (string Node, string Port)> { ["a"] = ("up", "port") });
        var edges = NodeRelationDeriver.DeriveEdgesFromRelationsJson(Wf, "n", json);

        Assert.AreEqual(2, edges.Count, "有映射 + 有溯源应推导 KeyMappingToNode + NodeToNode 两条边");
        Assert.IsTrue(edges.Any(e => e.Type == NodeRelationType.KeyMappingToNode));
        Assert.IsTrue(edges.Any(e => e.Type == NodeRelationType.NodeToNode
            && string.Equals(e.SourceId, NodeRelationFactory.NodeId(Wf, "up"), StringComparison.Ordinal)
            && string.Equals(e.TargetId, NodeRelationFactory.NodeId(Wf, "n"), StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ReferenceGrounding_EnsureTaskNodeLinkAsync_WritesCrossDbLinkEdge()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var grounding = new ReferenceGrounding(graph);

        await grounding.EnsureTaskNodeLinkAsync("task-s1", Wf, "cline_execute");

        // task 端点应注册（幂等 EnsureTaskMirrorAsync）。
        var taskRes = await graph.GetResourceAsync(new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = "task-s1", Location = Wf });
        Assert.IsNotNull(taskRes, "task 端点应注册");

        // task → node 的 cross_db_link 归属边应写入（复合节点引用关系：task 作用域寻址的归属边）。
        // 注意：cross_db_link 不落入 NodeRelationType（KeyMappingToNode/NodeToNode），走独立 AddReferenceAsync 加法路径，
        // 故 GetRelationsAsync 会跳过它，不影响数据流权威边集对账（写透守卫不会误删本归属边）。
        var refs = await graph.GetReferencesAsync(
            new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = "task-s1", Location = Wf });
        var link = refs.SingleOrDefault(r =>
            string.Equals(r.RefType, CrossDbReferenceTypes.CrossDbLink, StringComparison.Ordinal)
            && string.Equals(r.Target?.Id, NodeRelationFactory.NodeId(Wf, "cline_execute"), StringComparison.Ordinal));
        Assert.IsNotNull(link, "task→node 的 cross_db_link 归属边应写入");
    }

    [TestMethod]
    public async Task ReferenceGrounding_GetTaskNodeKeysAsync_ReturnsTaskOwnedNodesIsolatedPerTask()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var grounding = new ReferenceGrounding(graph);

        // 两个 task 各自归属节点；cline_execute 被两个 task 共同引用（隔离验证：各 task 只取其 own cross_db_link 边）。
        await grounding.EnsureTaskNodeLinkAsync("task-s1", Wf, "cline_execute");
        await grounding.EnsureTaskNodeLinkAsync("task-s1", Wf, "guard_output");
        await grounding.EnsureTaskNodeLinkAsync("task-s2", Wf, "answer");

        // task 作用域寻址：task-s1 只取自己归属的节点（不混入 task-s2 的 answer）——根除跨 task 污染。
        var keys = await grounding.GetTaskNodeKeysAsync("task-s1", Wf);
        CollectionAssert.AreEquivalent(new[] { "cline_execute", "guard_output" }, keys.OrderBy(k => k).ToList(),
            "task-s1 应只返回自身 cross_db_link 归属的节点，不混入其他 task 节点");

        var keys2 = await grounding.GetTaskNodeKeysAsync("task-s2", Wf);
        CollectionAssert.AreEquivalent(new[] { "answer" }, keys2.OrderBy(k => k).ToList());

        // 无 cross_db_link 边的 task → 空。
        var empty = await grounding.GetTaskNodeKeysAsync("task-none", Wf);
        Assert.AreEqual(0, empty.Count, "无归属边的 task 应返回空");
    }

    [TestMethod]
    public async Task ReferenceGrounding_ProjectTaskNodesAsync_OrdersByNodeToNodeEdge()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var grounding = new ReferenceGrounding(graph);

        // task 挂载三个节点（cross_db_link），其中 cline_execute → answer 有 NodeToNode 组合边。
        await grounding.EnsureTaskNodeLinkAsync("task-s1", Wf, "cline_execute");
        await grounding.EnsureTaskNodeLinkAsync("task-s1", Wf, "answer");
        await grounding.EnsureTaskNodeLinkAsync("task-s1", Wf, "guard_output");
        await graph.AddRelationAsync(Wf, NodeRelationFactory.NodeToNode(Wf, "cline_execute", "exec_result", "answer", "input"));

        var projected = await grounding.ProjectTaskNodesAsync("task-s1", Wf);

        // 顺序由 LiteDB 引用边决定：组合边上游 cline_execute 应排在依赖它的 answer 之前（DFS 后序拓扑）。
        var order = projected.Select(p => p.NodeKey).ToList();
        Assert.AreEqual(3, order.Count, "候选清单应含全部挂载节点");
        Assert.IsTrue(order.IndexOf("cline_execute") < order.IndexOf("answer"),
            "NodeToNode 组合边上游应排在依赖它的下游之前（节点顺序由 LiteDB 引用边决定）");
        Assert.IsTrue(projected.All(p => p.Order >= 1), "候选清单项应带顺序号");

        // 不携带载荷（索引级）：候选清单仅 nodeKey + 顺序，供 selection 层按需加载。
    }

    [TestMethod]
    public async Task ReferenceGrounding_ProjectTaskNodesAsync_HandlesDependencyCycleStably()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var grounding = new ReferenceGrounding(graph);

        // 主循环组合边环：provide_prompt → cline_execute（system/l1/l2 本轮）+ cline_execute → provide_prompt
        //（exec_result 跨轮）构成依赖环；rule_match 无组合入边（独立）。
        await grounding.EnsureTaskNodeLinkAsync("task-s1", Wf, "provide_prompt");
        await grounding.EnsureTaskNodeLinkAsync("task-s1", Wf, "cline_execute");
        await grounding.EnsureTaskNodeLinkAsync("task-s1", Wf, "rule_match");
        await graph.AddRelationAsync(Wf, NodeRelationFactory.NodeToNode(Wf, "provide_prompt", "system", "cline_execute", "system"));
        await graph.AddRelationAsync(Wf, NodeRelationFactory.NodeToNode(Wf, "cline_execute", "exec_result", "provide_prompt", "exec_result"));

        // 环处理：Kahn 拓扑对入度 0 节点严格定序（rule_match），环内（provide_prompt/cline_execute）按稳定序追加。
        // 多次投影结果应确定一致（不崩溃、不依赖枚举序）。
        var a = (await grounding.ProjectTaskNodesAsync("task-s1", Wf)).Select(p => p.NodeKey).ToList();
        var b = (await grounding.ProjectTaskNodesAsync("task-s1", Wf)).Select(p => p.NodeKey).ToList();
        CollectionAssert.AreEqual(a, b, "投影结果应确定一致（环内稳定序）");
        Assert.AreEqual(3, a.Count, "候选清单应含全部挂载节点");
        Assert.AreEqual("rule_match", a[0], "入度 0 的独立节点应严格排前（上游先于下游）");
    }

    private sealed class TestPoolItem : ITaskPoolItem
    {
        public string TaskKey { get; set; } = string.Empty;
        public int Priority { get; set; }
        public int MaxRetries { get; set; }
    }
}




