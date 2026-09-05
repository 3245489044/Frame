using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Sqlite;
using Share.Conversation;

namespace Share.StateManagement.Tests;

/// <summary>
/// SqliteHistoryStore（ur_histories 持久化会话历史）存储层测试：
/// ConversationLog 经 HistoryDoc（ThreadId/SessionId/Conversation）包装落 NodeKey+Json 的 round-trip。
/// </summary>
[TestClass]
public class SqliteHistoryStoreTests
{
    private static string NewDb() => Path.Combine(Path.GetTempPath(), $"hist_{System.Guid.NewGuid():N}.db");

    private static SqliteHistoryStore Open(string db, string prefix = "ur_")
        => new(new SqliteSession($"Data Source={db};Version=3;"), prefix);

    [TestMethod]
    public async Task Upsert_Get_Roundtrip_Preserves_Messages()
    {
        var db = NewDb();
        try
        {
            var store = Open(db);
            await store.InitializeAsync();

            var log = new ConversationLog();
            log.AppendUser("hello");
            log.AppendAssistant("hi there");
            await store.UpsertAsync("sess-1", log);

            var got = await store.GetAsync("sess-1");
            Assert.IsNotNull(got);
            Assert.AreEqual(2, got!.Messages.Count);
            Assert.AreEqual("hello", got.Messages[0].Text);
            Assert.AreEqual("hi there", got.Messages[1].Text);
            Assert.AreEqual(1, got.NextRound);

            var ids = await store.ListHistoryIdsAsync();
            Assert.AreEqual(1, ids.Count);
            Assert.AreEqual("sess-1", ids[0]);

            Assert.IsTrue(await store.DeleteAsync("sess-1"));
            Assert.IsNull(await store.GetAsync("sess-1"));
        }
        finally { File.Delete(db); }
    }

    [TestMethod]
    public async Task Upsert_Overwrites_Existing_Session()
    {
        var db = NewDb();
        try
        {
            var store = Open(db);
            await store.InitializeAsync();

            var log1 = new ConversationLog();
            log1.AppendUser("first");
            await store.UpsertAsync("sess-1", log1);

            var log2 = new ConversationLog();
            log2.AppendUser("second");
            await store.UpsertAsync("sess-1", log2);

            var got = await store.GetAsync("sess-1");
            Assert.IsNotNull(got);
            Assert.AreEqual(1, got!.Messages.Count);
            Assert.AreEqual("second", got.Messages[0].Text);
        }
        finally { File.Delete(db); }
    }
}