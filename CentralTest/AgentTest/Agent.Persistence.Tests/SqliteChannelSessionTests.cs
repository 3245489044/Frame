using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Agent.Persistence.Tests;

/// <summary>
/// 库级单写通道执行器（SqliteChannelSession，架构 I2「每库单写通道」落库原语）回归：
/// 写（ExecuteNonQuery / ExecuteInsertReturningId）经 SqliteWriteChannel 单写连接 autocommit；
/// 读（ExecuteQuery / ExecuteScalar）经 WAL 只读连接。本测试验证写后读回一致（通道写 + 读连接可见）。
/// 注：SqliteWriteChannel 常驻该库单写连接，故不删除临时库文件（测试进程退出即释放）。
/// </summary>
[TestClass]
public class SqliteChannelSessionTests
{
    [TestMethod]
    public void WriteRead_Roundtrip_ThroughChannel_AndReadConnection()
    {
        var db = Path.Combine(Path.GetTempPath(), "chansession_" + Guid.NewGuid().ToString("N") + ".db");
        var cs = "Data Source=" + db + ";Version=3;";
        var s = new SqliteChannelSession(ThreadingSqliteWriteHost.Shared, cs);
        var create = s.ExecuteNonQuery("CREATE TABLE IF NOT EXISTS t (Id INTEGER PRIMARY KEY AUTOINCREMENT, K TEXT)");
        var id = s.ExecuteInsertReturningId("INSERT INTO t (K) VALUES (@k)", new Dictionary<string, object> { { "@k", "v1" } });
        Assert.IsTrue(id > 0, "写入应返回自增 Id");

        // 读经 WAL 只读连接应看到通道写的结果。
        var scalar = s.ExecuteScalar("SELECT K FROM t WHERE Id=@id", new Dictionary<string, object> { { "@id", id } });
        Assert.AreEqual("v1", scalar);

        var rows = s.ExecuteQuery("SELECT K FROM t");
        Assert.AreEqual(1, rows.Count);
    }
}
