using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Agent.Persistence.Core;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Configuration;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using Operation.DataStoreDriven.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlowMethod.LoopFramework.Axioms;
using TestInfrastructure;

namespace Agent.Persistence.Tests;

[TestClass]
public class SnapshotPersistenceTests : IntegrationTestBase
{
    private static string _dbDir = string.Empty;
    private static IPersistenceStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbDir = CreateTempDirectory();
        var options = new PersistenceOptions
        {
            Provider = PersistenceProvider.Sqlite,
            DatabasePath = Path.Combine(_dbDir, "snap.db"),
            TablePrefix = "ur_",
            AutoCreateSchema = true,
        };
        _store = SqlitePersistenceStore.Create(options);
        _store.InitializeAsync().GetAwaiter().GetResult();
    }

    [TestCleanup]
    public override void TestCleanup()
    {
        _store?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.TestCleanup();
    }

    [TestMethod]
    public async Task Snapshot_SaveLoad_Roundtrip()
    {
        await _store.Snapshots.SaveAsync("thread-1", "sess-1", "{\"round\":3}");

        // 快照随 ur_tasks 的 Json 持久化，ThreadId/SessionId 注入 envelope（供无 sessionId 按 ThreadId 检索）——故按语义断言而非字面串。
        var json = await _store.Snapshots.LoadAsync("thread-1", "sess-1");
        var node = JsonNode.Parse(json!)!.AsObject();
        Assert.AreEqual(3, node["round"]!.GetValue<int>(), "快照业务负载应保真读回");
        Assert.AreEqual("thread-1", node["ThreadId"]!.GetValue<string>(), "ThreadId 应注入快照 envelope");
        Assert.AreEqual("sess-1", node["SessionId"]!.GetValue<string>(), "SessionId 应注入快照 envelope");

        // 覆写后读最新
        await _store.Snapshots.SaveAsync("thread-1", "sess-1", "{\"round\":4}");
        json = await _store.Snapshots.LoadAsync("thread-1", "sess-1");
        node = JsonNode.Parse(json!)!.AsObject();
        Assert.AreEqual(4, node["round"]!.GetValue<int>(), "覆写后应读回最新快照业务负载");
    }

    [TestMethod]
    public async Task Snapshot_LoadLatestWhenNoSession()
    {
        await _store.Snapshots.SaveAsync("thread-x", "a", "{\"round\":1}");
        await _store.Snapshots.SaveAsync("thread-x", "b", "{\"round\":2}");

        var json = await _store.Snapshots.LoadAsync("thread-x", null);
        Assert.IsNotNull(json);
    }

    [TestMethod]
    public async Task Snapshot_LoadReturnsNullWhenMissing()
    {
        var json = await _store.Snapshots.LoadAsync("missing-thread", "sess-1");
        Assert.IsNull(json);
    }

    [TestMethod]
    public async Task Snapshot_LoadEmptySnapshot_ReturnsNull()
    {
        // B3 防护：空快照（空串/DBNull）→ null（「无快照」语义），不返回空串导致「null 表示无快照」判定失准。
        await _store.Snapshots.SaveAsync("thread-empty", "s1", "");
        Assert.IsNull(await _store.Snapshots.LoadAsync("thread-empty", "s1"), "空快照应读为 null（无快照语义）");
        // 覆写为真实快照后应可读回（含 envelope 注入）。
        await _store.Snapshots.SaveAsync("thread-empty", "s1", "{\"round\":9}");
        var node = JsonNode.Parse((await _store.Snapshots.LoadAsync("thread-empty", "s1"))!)!.AsObject();
        Assert.AreEqual(9, node["round"]!.GetValue<int>(), "覆写为真实快照后应可读回业务负载");
    }

    [TestMethod]
    public void SnapshotSerializer_AxiomContextRoundtrip()
    {
        var ctx = new AxiomContext { UserInput = "hi", Round = 2, LastEval = EvalOutcome.Partial, Scope = Scope.LoopBody };
        ctx.Token.Max = 5;
        ctx.Token.Advance();
        ctx.Token.Advance();
        ctx.Budget.Escalate();
        ctx.Data["k"] = "v";

        var json = SnapshotSerializer.Serialize(ctx);
        var restored = SnapshotSerializer.Deserialize(json);

        Assert.AreEqual(2, restored.Round);
        Assert.AreEqual(2, restored.Token.Value);
        Assert.AreEqual(5, restored.Token.Max);
        Assert.AreEqual(1, restored.Budget.Level);
        Assert.AreEqual(EvalOutcome.Partial, restored.LastEval);
        Assert.AreEqual("v", restored.Data["k"]);
    }
}
