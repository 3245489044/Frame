using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Sqlite;
using Share.TaskStatusPool.TaskPool;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace MarkdownViewer.Tests;

[TestClass]
public class TaskPoolPagedReadTests
{
    private sealed record PoolItem(string TaskKey, int Priority = 0, int MaxRetries = 0, string? UserInput = null) : ITaskPoolItem;

    private static SqliteTaskPoolStore<PoolItem> Store(string db)
        => new(new SqliteSession($"Data Source={db};Version=3;"), "ur_");

    private static string NewDb() => Path.Combine(Path.GetTempPath(), $"tp_{System.Guid.NewGuid():N}.db");

    [TestMethod]
    public async Task SqliteStore_GetPageRowsAsync_PagesAndCounts()
    {
        var db = NewDb();
        try
        {
            var store = Store(db);
            await store.InitializeAsync();
            for (var i = 0; i < 5; i++)
                await store.EnqueueAsync(new PoolItem($"k{i}", Priority: 5 - i));

            var page = await store.GetPageRowsAsync(1, 2);
            Assert.AreEqual(5, page.Total);
            Assert.AreEqual(2, page.Items.Count);
            // 排序 Priority DESC：k0(5) k1(4) k2(3) k3(2) k4(1) → offset=1 取 k1,k2
            Assert.AreEqual("k1", page.Items[0].TaskKey);
            Assert.AreEqual("k2", page.Items[1].TaskKey);
        }
        finally { File.Delete(db); }
    }

    [TestMethod]
    public async Task SqliteStore_GetPageAsync_FiltersByStatus()
    {
        var db = NewDb();
        try
        {
            var store = Store(db);
            await store.InitializeAsync();
            await store.EnqueueAsync(new PoolItem("a"));
            await store.EnqueueAsync(new PoolItem("b"));
            await store.UpdateStatusAsync("b", TaskExecutionStatus.NotStarted, TaskExecutionStatus.Completed);

            var page = await store.GetPageAsync(0, 10, TaskExecutionStatus.Completed);
            Assert.AreEqual(1, page.Total);
            Assert.AreEqual("b", page.Items[0].TaskKey);
        }
        finally { File.Delete(db); }
    }

    [TestMethod]
    public async Task InMemoryStore_GetPageAsync_Works()
    {
        var store = new InMemoryTaskPoolPersistence<PoolItem>();
        await store.EnqueueAsync(new PoolItem("a", Priority: 2));
        await store.EnqueueAsync(new PoolItem("b", Priority: 1));
        await store.EnqueueAsync(new PoolItem("c", Priority: 0));

        var page = await store.GetPageAsync(1, 1);
        Assert.AreEqual(3, page.Total);
        Assert.AreEqual("b", page.Items[0].TaskKey); // Priority DESC：a(2) b(1) c(0) → offset=1=b
    }

    [TestMethod]
    public async Task SqliteStore_PayloadJson_ChineseLiteral_AfterJsonOptionsFix()
    {
        var db = NewDb();
        try
        {
            var store = Store(db);
            await store.InitializeAsync();
            await store.EnqueueAsync(new PoolItem("cn", UserInput: "最小测试"));

            var page = await store.GetPageRowsAsync(0, 10);
            var raw = page.Items[0].PayloadJson!;
            StringAssert.Contains(raw, "最小测试");
            Assert.IsFalse(raw.Contains("\\u6700", StringComparison.Ordinal));
        }
        finally { File.Delete(db); }
    }
}
