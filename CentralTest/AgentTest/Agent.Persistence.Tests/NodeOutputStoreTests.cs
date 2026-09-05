using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Core.Nodes;
using Agent.Contract;
using Agent.Contract.Nodes;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Configuration;
using Operation.DataStoreDriven.Sqlite;
using Operation.HierarchyMethod.Nodes;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using UniversalWorkflowFramework.StorageFramework.Sqlite;
using TestInfrastructure;

namespace Agent.Persistence.Tests;

/// <summary>
/// 节点输出存储（模块 A 并入模块 C）回归保护：节点输出并入业务节点（NodeType=node，NodeKey=裸 {node}）持久化，
/// 值经 <see cref="NodeOutputCodec"/> 类型保真往返；重启（重开同一 db）可恢复；按 ThreadId 过滤读取。
/// </summary>
[TestClass]
public class NodeOutputStoreTests : IntegrationTestBase
{
    private string _dir = string.Empty;
    private SqliteNodeStore _nodes = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = CreateTempDirectory();
        var options = new PersistenceOptions
        {
            Provider = PersistenceProvider.Sqlite,
            DatabasePath = Path.Combine(_dir, "nodes.db"),
            TablePrefix = "ur_",
            AutoCreateSchema = true,
        };
        _nodes = new SqliteNodeStore(new SqliteSession(new SqliteConnectionFactory(options).ConnectionString), "ur_nodes");
        _nodes.InitializeAsync().GetAwaiter().GetResult();
    }

    [TestMethod]
    public async Task WriteOutput_RoundTrips_Typed_Values_And_Persists_Across_Store_Instances()
    {
        var store = new NodeOutputStore(_nodes);
        var cline = new ClineResult(true, "hello", null, SessionId: "s1", OutputState: "done", StopReason: "complete");

        await store.WriteOutputAsync("wf", "n1", "text", "value");
        await store.WriteOutputAsync("wf", "n1", "count", 42);
        await store.WriteOutputAsync("wf", "n1", "flag", true);
        await store.WriteOutputAsync("wf", "n2", "exec", cline);
        await store.WriteOutputAsync("wf", "n2", "ignored", null!); // null 不落盘（不产生空槽位）

        // 单节点读取（类型保真还原）
        var n2 = await store.TryGetNodeOutputAsync("wf", "n2");
        Assert.IsNotNull(n2);
        Assert.AreEqual("n2", n2!.NodeKey);
        var exec = Assert.IsInstanceOfType<ClineResult>(n2.OutputPorts["exec"]);
        Assert.AreEqual("s1", exec.SessionId);
        Assert.AreEqual("complete", exec.StopReason);

        // 枚举（含多节点、多值类型）
        var all = await store.EnumerateOutputsAsync("wf");
        Assert.AreEqual(2, all.Count);
        var n1 = all.Single(o => o.NodeKey == "n1");
        Assert.AreEqual("value", n1.OutputPorts["text"]);
        Assert.AreEqual(42, n1.OutputPorts["count"]);
        Assert.AreEqual(true, n1.OutputPorts["flag"]);
        Assert.IsFalse(n1.OutputPorts.ContainsKey("ignored"), "null 值不应产生端口槽位");

        // 节点键枚举
        var keys = await store.EnumerateNodeKeysAsync("wf");
        CollectionAssert.AreEquivalent(new[] { "n1", "n2" }, keys.OrderBy(k => k).ToList());

        // 持久化验证：重开同一 db 的新 store，数据应完整可恢复
        var reopen = new SqliteNodeStore(
            new SqliteSession(new SqliteConnectionFactory(new PersistenceOptions
            {
                Provider = PersistenceProvider.Sqlite,
                DatabasePath = Path.Combine(_dir, "nodes.db"),
                TablePrefix = "ur_",
            }).ConnectionString), "ur_nodes");
        reopen.InitializeAsync().GetAwaiter().GetResult();
        var restored = await new NodeOutputStore(reopen).EnumerateOutputsAsync("wf");
        Assert.AreEqual(2, restored.Count);
        Assert.AreEqual(42, restored.Single(o => o.NodeKey == "n1").OutputPorts["count"]);
        Assert.IsInstanceOfType<ClineResult>(restored.Single(o => o.NodeKey == "n2").OutputPorts["exec"]);
    }

    [TestMethod]
    public async Task WriteOutput_MultiplePorts_On_Same_Node_Accumulates_Not_Overwrites()
    {
        var store = new NodeOutputStore(_nodes);
        await store.WriteOutputAsync("wf", "n1", "a", 1);
        await store.WriteOutputAsync("wf", "n1", "b", 2);

        var n1 = await store.TryGetNodeOutputAsync("wf", "n1");
        Assert.IsNotNull(n1);
        Assert.AreEqual(2, n1!.OutputPorts.Count, "同一节点多端口应累加而非互相覆盖");
        Assert.AreEqual(1, n1.OutputPorts["a"]);
        Assert.AreEqual(2, n1.OutputPorts["b"]);

        // 重复写同端口应覆写（不删除其他端口）
        await store.WriteOutputAsync("wf", "n1", "a", 99);
        n1 = await store.TryGetNodeOutputAsync("wf", "n1");
        Assert.AreEqual(2, n1!.OutputPorts.Count, "覆写同端口不应移除其他端口");
        Assert.AreEqual(99, n1.OutputPorts["a"]);
        Assert.AreEqual(2, n1.OutputPorts["b"]);
    }

    [TestMethod]
    public async Task ClearNode_And_ClearWorkflow_Remove_Output_Records()
    {
        var store = new NodeOutputStore(_nodes);
        await store.WriteOutputAsync("wf", "n1", "a", 1);
        await store.WriteOutputAsync("wf", "n2", "b", 2);

        await store.ClearNodeAsync("wf", "n1");
        Assert.IsNull(await store.TryGetNodeOutputAsync("wf", "n1"));
        Assert.IsNotNull(await store.TryGetNodeOutputAsync("wf", "n2"), "清除单节点不应影响其他节点");

        await store.ClearWorkflowAsync("wf");
        Assert.AreEqual(0, (await store.EnumerateOutputsAsync("wf")).Count);
    }

    [TestMethod]
    public async Task WorkflowId_Is_Namespaced_Nodes_Do_Not_Leak_Across_Workflows()
    {
        // 输出并入业务节点后：业务节点 NodeKey=裸 {node}（主键唯一，ThreadId 承载 wf 归属），
        // 命名空间隔离由 ThreadId 枚举保证 —— 不同 wf 下各自归属，互不泄漏。
        var store = new NodeOutputStore(_nodes);
        await store.WriteOutputAsync("wf1", "n1", "a", 1);
        await store.WriteOutputAsync("wf2", "n2", "a", 2);

        var wf1 = await store.EnumerateOutputsAsync("wf1");
        Assert.AreEqual(1, wf1.Count);
        Assert.AreEqual("n1", wf1[0].NodeKey);
        Assert.AreEqual(1, wf1[0].OutputPorts["a"]);
        var wf2 = await store.EnumerateOutputsAsync("wf2");
        Assert.AreEqual(1, wf2.Count);
        Assert.AreEqual("n2", wf2[0].NodeKey);
        Assert.AreEqual(2, wf2[0].OutputPorts["a"]);
    }

    [TestMethod]
    public async Task WriteSignalMetadata_And_EnumerateSignals_Sorts_By_Rank_And_Filters()
    {
        var store = new NodeOutputStore(_nodes);
        // 端口 + 信号元数据：n1（sticky，低分）、n2（高 valueScore）、n3（低分最新）、n4（module=plan）
        await store.WriteOutputAsync("wf", "n1", "a", 1);
        await store.WriteOutputAsync("wf", "n2", "a", 2);
        await store.WriteOutputAsync("wf", "n3", "a", 3);
        await store.WriteOutputAsync("wf", "n4", "a", 4);
        await store.WriteSignalMetadataAsync("wf", "n1", new NodeSignal("n1", "wf", Module: "task", Region: "task.proj-x", NodeType: "task_node", Freshness: 1, ValueScore: 0.1, Sticky: true, Priority: 5));
        await store.WriteSignalMetadataAsync("wf", "n2", new NodeSignal("n2", "wf", Module: "task", Region: "task.proj-x", NodeType: "task_node", Freshness: 3, ValueScore: 0.9));
        await store.WriteSignalMetadataAsync("wf", "n3", new NodeSignal("n3", "wf", Module: "task", Region: "task.proj-x", NodeType: "task_node", Freshness: 100, ValueScore: 0.2));
        await store.WriteSignalMetadataAsync("wf", "n4", new NodeSignal("n4", "wf", Module: "plan", Region: "plan.proj-x", NodeType: "plan_node", Freshness: 50, ValueScore: 0.8));

        // 信号 rank：sticky 恒最前 → valueScore desc → freshness desc。
        var all = await store.EnumerateSignalsAsync("wf");
        Assert.AreEqual(4, all.Count);
        Assert.AreEqual("n1", all[0].NodeKey, "sticky 节点应恒最前");
        Assert.AreEqual("n2", all[1].NodeKey, "valueScore 0.9 应次前");
        Assert.AreEqual("n4", all[2].NodeKey, "valueScore 0.8 第三");
        Assert.AreEqual("n3", all[3].NodeKey, "valueScore 0.2 最后（freshness 大者在其组内）");

        // module / region / nodeType 精确过滤。
        var plan = await store.EnumerateSignalsAsync("wf", module: "plan");
        Assert.AreEqual(1, plan.Count);
        Assert.AreEqual("n4", plan[0].NodeKey);
        var taskRegion = await store.EnumerateSignalsAsync("wf", region: "task.proj-x", nodeType: "task_node");
        Assert.AreEqual(3, taskRegion.Count, "task 区域 + task_node 类别应命中 3 个");

        // limit 截断。
        var top2 = await store.EnumerateSignalsAsync("wf", limit: 2);
        Assert.AreEqual(2, top2.Count);
        Assert.AreEqual("n1", top2[0].NodeKey);
        Assert.AreEqual("n2", top2[1].NodeKey);

        // 信号元数据不覆盖端口；读取仍完整还原。
        var n1 = await store.TryGetNodeOutputAsync("wf", "n1");
        Assert.IsNotNull(n1);
        Assert.AreEqual(1, n1!.OutputPorts["a"], "写信号元数据不应覆盖既有端口");
        Assert.AreEqual(true, n1.Sticky);
        Assert.AreEqual(0.1, n1.ValueScore);
    }

    [TestMethod]
    public void NodeOutputCodec_RoundTrips_Common_Types()
    {
        Assert.AreEqual(42, NodeOutputCodec.DecodeValue(NodeOutputCodec.EncodeValue(42)));
        Assert.AreEqual("x", NodeOutputCodec.DecodeValue(NodeOutputCodec.EncodeValue("x")));
        Assert.AreEqual(true, NodeOutputCodec.DecodeValue(NodeOutputCodec.EncodeValue(true)));
        Assert.IsNull(NodeOutputCodec.DecodeValue(NodeOutputCodec.EncodeValue(null)));

        // object 承载的嵌套字典：System.Text.Json 将嵌套值还原为 JsonElement（非原始运行时类型）。
        // 这是 object 型容器的 STJ 固有局限——主流程节点输出值（string/数值/ClineResult 等具体类型）不受影响，类型保真往返。
        var dict = new Dictionary<string, object> { ["k"] = "v" };
        var round = NodeOutputCodec.DecodeValue(NodeOutputCodec.EncodeValue(dict));
        Assert.IsInstanceOfType<Dictionary<string, object>>(round);
        Assert.AreEqual("v", ((Dictionary<string, object>)round!)["k"].ToString());
    }
}
