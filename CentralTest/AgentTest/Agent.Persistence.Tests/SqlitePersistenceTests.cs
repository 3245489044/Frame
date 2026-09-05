using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Configuration;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using Operation.DataStoreDriven.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UniversalWorkflowFramework.Core.Models;
using TestInfrastructure;

namespace Agent.Persistence.Tests;

[TestClass]
public class SqlitePersistenceTests : IntegrationTestBase
{
    private static string _dbDir = string.Empty;
    private static string _dbPath = string.Empty;
    private static PersistenceOptions _options = null!;
    private static IPersistenceStore _store = null!;
    private static SqliteSession _session = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbDir = CreateTempDirectory();
        _dbPath = Path.Combine(_dbDir, "test.db");

        _options = new PersistenceOptions
        {
            Provider = PersistenceProvider.Sqlite,
            DatabasePath = _dbPath,
            TablePrefix = "ur_",
            AutoCreateSchema = true,
        };

        _store = SqlitePersistenceStore.Create(_options);
        _store.InitializeAsync().GetAwaiter().GetResult();
        _session = new SqliteSession(new SqliteConnectionFactory(_options).ConnectionString);
    }

    [TestCleanup]
    public override void TestCleanup()
    {
        _store?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.TestCleanup();
    }

    [TestMethod]
    public void Initialize_CreatesAllUrTables()
    {
        // 三库收敛：SQL 仅 ur_tasks（状态集合）。引用关系真源为 LiteDB 图库，无 SQL ur_resources 表。
        var expected = new[] { "ur_tasks" };
        foreach (var table in expected)
        {
            var count = _session.ExecuteScalar(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name",
                new Dictionary<string, object> { ["@name"] = table });
            Assert.AreEqual(1L, count, $"表 {table} 未创建");
        }
    }

    [TestMethod]
    public void Initialize_IsIdempotent()
    {
        _store.InitializeAsync().GetAwaiter().GetResult();
        var count = _session.ExecuteScalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table'");
        Assert.IsTrue(Convert.ToInt64(count) >= 2);
    }

    [TestMethod]
    public async Task Transaction_CommitPersists()
    {
        await using (var tx = await _store.BeginTransactionAsync())
        {
            // 当前 ur_tasks 为纯「NodeKey + 单个 Json」（KvJson 三列），不再有旧版 ThreadId/SessionId/SnapshotJson 物理列。
            tx.Commands.ExecuteNonQuery(
                "INSERT INTO [ur_tasks] ([NodeKey],[Json]) VALUES (@k,@j)",
                new Dictionary<string, object> { ["@k"] = "commit-1", ["@j"] = "{\"round\":3}" });
            await tx.CommitAsync();
        }

        var json = await _store.Snapshots.LoadAsync("t", "commit-1");
        Assert.AreEqual("{\"round\":3}", json, "事务提交后按 sessionId 应读到已落库快照");
    }

    [TestMethod]
    public async Task Transaction_ExplicitRollbackUndoes()
    {
        await using (var tx = await _store.BeginTransactionAsync())
        {
            tx.Commands.ExecuteNonQuery(
                "INSERT INTO [ur_tasks] ([NodeKey],[Json]) VALUES (@k,@j)",
                new Dictionary<string, object> { ["@k"] = "rollback-1", ["@j"] = "{\"round\":3}" });
            await tx.RollbackAsync();
        }

        var json = await _store.Snapshots.LoadAsync("t", "rollback-1");
        Assert.IsNull(json, "事务显式回滚后写入应被撤销，读取为 null");
    }

    [TestMethod]
    public async Task Transaction_DisposeWithoutCommitRollsBack()
    {
        await using (var tx = await _store.BeginTransactionAsync())
        {
            tx.Commands.ExecuteNonQuery(
                "INSERT INTO [ur_tasks] ([NodeKey],[Json]) VALUES (@k,@j)",
                new Dictionary<string, object> { ["@k"] = "dispose-1", ["@j"] = "{\"round\":3}" });
            // 不提交，dispose 时自动回滚
        }

        var json = await _store.Snapshots.LoadAsync("t", "dispose-1");
        Assert.IsNull(json, "事务未提交即释放应自动回滚，读取为 null");
    }
}
