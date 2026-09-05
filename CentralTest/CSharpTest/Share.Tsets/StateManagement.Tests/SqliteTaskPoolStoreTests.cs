using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Sqlite;
using Share.StateManagement;
using Share.TaskStatusPool.TaskPool;

namespace Share.StateManagement.Tests;

/// <summary>
/// SqliteTaskPoolStore（ur_tasks 持久化任务池）存储层测试：
/// 载荷/状态/重试字段落盘还原、条件状态迁移、重启重枚举未完成任务（ProcessPending 回填的数据基础）。
/// </summary>
[TestClass]
public class SqliteTaskPoolStoreTests
{
    private sealed record PoolItem(string TaskKey, int Priority = 0, int MaxRetries = 0) : ITaskPoolItem;

    private static string NewDb() => Path.Combine(Path.GetTempPath(), $"tp_{System.Guid.NewGuid():N}.db");

    private static SqliteTaskPoolStore<PoolItem> Open(string db, string prefix = "ur_")
        => new(new SqliteSession($"Data Source={db};Version=3;"), prefix);

    [TestMethod]
    public async Task Enqueue_List_Get_Roundtrip()
    {
        var db = NewDb();
        try
        {
            var store = Open(db);
            await store.InitializeAsync();
            await store.EnqueueAsync(new PoolItem("k1", Priority: 5, MaxRetries: 2));

            var listed = await store.ListAsync(TaskExecutionStatus.NotStarted);
            Assert.AreEqual(1, listed.Count);
            Assert.AreEqual("k1", listed[0].TaskKey);
            Assert.AreEqual(5, listed[0].Priority);
            Assert.AreEqual(2, listed[0].MaxRetries);

            var got = await store.GetAsync("k1");
            Assert.IsNotNull(got);
            Assert.AreEqual("k1", got!.TaskKey);
            Assert.AreEqual(2, got.MaxRetries);
        }
        finally { File.Delete(db); }
    }

    [TestMethod]
    public async Task UpdateStatus_Conditional_Transition()
    {
        var db = NewDb();
        try
        {
            var store = Open(db);
            await store.InitializeAsync();
            await store.EnqueueAsync(new PoolItem("k1"));

            // from=NotStarted → Running 成功；再次用 from=NotStarted（当前 Running）→ 条件不匹配返回 false
            Assert.IsTrue(await store.UpdateStatusAsync("k1", TaskExecutionStatus.NotStarted, TaskExecutionStatus.Running));
            Assert.IsFalse(await store.UpdateStatusAsync("k1", TaskExecutionStatus.NotStarted, TaskExecutionStatus.Completed));
            Assert.IsTrue(await store.UpdateStatusAsync("k1", TaskExecutionStatus.Running, TaskExecutionStatus.Completed));

            var done = await store.ListAsync(TaskExecutionStatus.Completed);
            Assert.AreEqual(1, done.Count);
        }
        finally { File.Delete(db); }
    }

    [TestMethod]
    public async Task Restart_ReEnumerates_Persisted_NotStarted()
    {
        var db = NewDb();
        try
        {
            var store = Open(db);
            await store.InitializeAsync();
            await store.EnqueueAsync(new PoolItem("k1", Priority: 3));
            await store.EnqueueAsync(new PoolItem("k2", Priority: 1));

            // 模拟进程重启：新实例（同一库文件）重枚举未完成任务，供 ProcessPending 回填
            var reopened = Open(db);
            await reopened.InitializeAsync();
            var pending = await reopened.ListAsync(TaskExecutionStatus.NotStarted);
            Assert.AreEqual(2, pending.Count);
        }
        finally { File.Delete(db); }
    }

    [TestMethod]
    public void Schema_Has_Only_Key_And_Snapshot_Columns()
    {
        var db = NewDb();
        try
        {
            var store = Open(db);
            store.InitializeAsync().GetAwaiter().GetResult();
            var session = new SqliteSession($"Data Source={db};Version=3;");
            var cols = session.ExecuteQuery("PRAGMA table_info(ur_tasks)");
            var names = cols
                .Select(c => (c["name"] as string) ?? string.Empty)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(n => n.ToLowerInvariant())
                .ToList();
            CollectionAssert.AreEqual(new[] { "id", "json", "nodekey" }, names,
                "ur_tasks should have only Id/NodeKey/Json columns; ThreadId/Status/Priority/MaxRetries/CreatedAt/UpdatedAt are persisted in Json");
        }
        finally { File.Delete(db); }
    }

    [TestMethod]
    public async Task Status_Priority_Timestamps_Persist_In_SnapshotJson()
    {
        var db = NewDb();
        try
        {
            var store = Open(db);
            await store.InitializeAsync();
            await store.EnqueueAsync(new PoolItem("k1", Priority: 7, MaxRetries: 3));

            // 状态/优先级经 json_extract 从 SnapshotJson 过滤还原（非 status-aware 条目注入 NotStarted）
            var pending = await store.ListAsync(TaskExecutionStatus.NotStarted);
            Assert.AreEqual(1, pending.Count);
            Assert.AreEqual(7, pending[0].Priority);
            Assert.AreEqual(3, pending[0].MaxRetries);

            // 时间戳随 SnapshotJson 持久化（__createdAt/__updatedAt），行视图可读
            var page = await store.GetPageRowsAsync(0, 10);
            Assert.AreEqual(1, page.Items.Count);
            Assert.AreEqual(7, page.Items[0].Priority, "Priority 应经 json_extract 从 SnapshotJson 派生");
            Assert.AreEqual(3, page.Items[0].MaxRetries, "MaxRetries 应经 json_extract 从 SnapshotJson 派生");
            Assert.IsFalse(string.IsNullOrWhiteSpace(page.Items[0].CreatedAt), "__createdAt 应随 SnapshotJson 持久化");
            Assert.IsFalse(string.IsNullOrWhiteSpace(page.Items[0].UpdatedAt), "__updatedAt 应随 SnapshotJson 持久化");

            // 状态迁移仍走 CAS（json_extract 条件）并落 JSON
            Assert.IsTrue(await store.UpdateStatusAsync("k1", TaskExecutionStatus.NotStarted, TaskExecutionStatus.Running));
            var running = await store.ListAsync(TaskExecutionStatus.Running);
            Assert.AreEqual(1, running.Count);
        }
        finally { File.Delete(db); }
    }

    [TestMethod]
    public async Task Initialize_Migrates_Legacy_FourColumn_Schema()
    {
        var db = NewDb();
        try
        {
            var session = new SqliteSession($"Data Source={db};Version=3;");
            session.ExecuteNonQuery("CREATE TABLE ur_tasks ([Id] INTEGER PRIMARY KEY AUTOINCREMENT,[ThreadId] TEXT NOT NULL,[SessionId] TEXT NOT NULL,[SnapshotJson] TEXT NOT NULL,UNIQUE([ThreadId],[SessionId]))");
            session.ExecuteNonQuery("INSERT INTO ur_tasks ([ThreadId],[SessionId],[SnapshotJson]) VALUES ('taskpool','legacy1','{\"TaskKey\":\"legacy1\",\"Priority\":9}')");

            var store = Open(db);
            await store.InitializeAsync();

            var got = await store.GetAsync("legacy1");
            Assert.IsNotNull(got);
            Assert.AreEqual(9, ((PoolItem)got!).Priority, "migrated: ThreadId/SessionId folded into Json, NodeKey=SessionId");

            var cols = session.ExecuteQuery("PRAGMA table_info(ur_tasks)")
                .Select(c => (c["name"] as string) ?? string.Empty)
                .Select(n => n.ToLowerInvariant())
                .ToArray();
            CollectionAssert.AreEquivalent(new[] { "id", "json", "nodekey" }, cols, "legacy 4-col should converge to NodeKey/Json");
        }
        finally { File.Delete(db); }
    }
}
