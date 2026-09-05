using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Configuration;
using Operation.DataStoreDriven.Core;
using Operation.DataStoreDriven.Graph;
using Operation.DataStoreDriven.Resolvers;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using TestInfrastructure;

namespace Agent.Persistence.Tests;

/// <summary>
/// 写透守卫覆盖面测试（Phase 0 回归护栏，暴露「绕组装漂移」）：
/// 断言 Sqlite 路径下 <see cref="IDataStore"/> 的 <c>Nodes / Zones / Config / Checkpoints</c> 各写面
/// 产出的节点都落<strong>同一</strong> ur_nodes 底层（经 <c>store.Nodes</c> 读回可见）。
/// 任一把 Zone/Config/Checkpoint 装配到不同节点库实例即漂移，本测试失败。
/// 对齐公理 SingleImplementation / RedundancyElimination（守卫覆盖整库写面的结构保证）。
/// </summary>
[TestClass]
public class WriteGuardSurfaceCoverageTests : IntegrationTestBase
{
    private static string _dir = string.Empty;

    [TestInitialize]
    public void Setup() => _dir = CreateTempDirectory();

    /// <summary>独立临时库文件，构造即打开；跨测试共享路径会文件锁冲突，故每条测试独立路径。</summary>
    private static PersistenceOptions Options() => new()
    {
        Provider = PersistenceProvider.Sqlite,
        DatabasePath = Path.Combine(_dir, $"wgsurf-{Guid.NewGuid():N}.db"),
        TablePrefix = "ur_",
        AutoCreateSchema = true,
    };

    [TestMethod]
    public async Task SqliteFacade_AllWriteSurfaces_LandInSingleUrNodes()
    {
        await using var store = DataStoreProviderFactory.Create(Options());
        await store.InitializeAsync();

        // 单一 ur_nodes 底层：任一写面（Zones/Config/Checkpoints/Nodes）的产出都应经它读回。
        var nodes = store.Nodes;
        const string thread = "wf-surf";

        // ① 三区（Zone）：写 input → 落 ur_nodes NodeType=input，经 store.Nodes 读回可见。
        await store.Zones.AppendInputAsync(new InputRecord { ThreadId = thread, InputKey = "k1", PayloadJson = "{\"a\":1}" });
        Assert.AreEqual(1, (await nodes.ListAsync(NodeTypes.Input, thread)).Count,
            "Zone 写必须落 Nodes 同一 ur_nodes 底层（绕组装漂移：Zone 直写非同一节点库则读回为空）。");

        // ② 配置（Config）：写 config.{key} 节点，经 store.Nodes 读回可见。
        await store.Config.SetAsync("surf.cfg", "v1");
        Assert.IsNotNull(await nodes.GetAsync(NodeKeyPrefix.Config + "surf.cfg"),
            "Config 写必须落 Nodes 同一 ur_nodes 底层（绕组装漂移）。");

        // ③ checkpoint：写 checkpoint.{thread}.{ns}.{cpId} 节点，经 store.Nodes 读回可见。
        await store.Checkpoints.PutAsync(new Checkpoint
        {
            ThreadId = thread,
            CheckpointNs = "ns",
            CheckpointId = "cp1",
            Step = 1,
        });
        Assert.IsNotNull(await nodes.GetAsync(NodeKeyPrefix.Checkpoint + thread + ".ns.cp1"),
            "Checkpoint 写必须落 Nodes 同一 ur_nodes 底层（绕组装漂移）。");

        // ④ 直接业务节点写（NodeType=node）：幂等 upsert 落 ur_nodes。
        await store.Nodes.PutAsync(new NodeRecord { NodeKey = "surf-node", NodeType = NodeTypes.Node, ThreadId = thread });
        Assert.IsNotNull(await nodes.GetAsync("surf-node"), "业务节点直写必须落 ur_nodes。");
    }

    [TestMethod]
    public async Task SqliteFacade_WithGrounding_WrapsWholeWriteSurfaceInGuard()
    {
        // 独立图库 + 引用守卫；注入工厂后，整库写面（Nodes/Zone/Config/Checkpoint）统一以守卫为底层（R2 结构保证）。
        var graphPath = Path.Combine(_dir, $"wgsurf-graph-{Guid.NewGuid():N}.db");
        await using var graph = ReferenceGraphCoordinator.CreateAsync(graphPath);
        var grounding = new ReferenceGrounding(graph);
        await using var store = DataStoreProviderFactory.Create(Options(), grounding: grounding);
        await store.InitializeAsync();

        Assert.IsInstanceOfType(store.Nodes, typeof(WriteGuardPort),
            "注入守卫后 Nodes 必须为 WriteGuardPort（守卫覆盖整库写面）。");

        // ① 业务节点写 → 自动建 wf.node 图镜像（经守卫，非"Zone 恰不需镜像"的巧合）。
        const string wf = "wf-guard";
        await store.Nodes.PutAsync(new NodeRecord { NodeKey = "g-node", NodeType = NodeTypes.Node, ThreadId = wf });
        Assert.IsNotNull(await graph.GetResourceAsync(new ResourceIdentifier
            {
                ResourceType = NodeResourceTypes.Node,
                Id = NodeRelationFactory.NodeId(wf, "g-node"),
                Location = wf,
            }),
            "注入守卫后业务节点写必须自动建图镜像（守卫覆盖整库写面）。");

        // ② 判别类型（zone input）写 → 仅落 ur_nodes，不建图镜像（守卫白名单仅 {node}，R3 语义不变）。
        await store.Zones.AppendInputAsync(new InputRecord { ThreadId = wf, InputKey = "k1" });
        var zoneNode = (await store.Nodes.ListAsync(NodeTypes.Input, wf)).FirstOrDefault();
        Assert.IsNotNull(zoneNode, "zone 写仍落 ur_nodes。");
        Assert.IsNull(await graph.GetResourceAsync(new ResourceIdentifier
            {
                ResourceType = NodeResourceTypes.Node,
                Id = NodeRelationFactory.NodeId(wf, zoneNode!.NodeKey),
                Location = wf,
            }),
            "判别类型（zone）写不应建图镜像（守卫白名单仅 {node}）。");
    }
}
