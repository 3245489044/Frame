using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Agent.Persistence.Tests;

/// <summary>
/// 库级写面持有者登记（SqliteWriteOwnerRegistry，进程内取证件）回归：
/// 精确 token 增删（无整队重建竞态/无虚报长持）· 并发进出后零残留 · 排除自身 · 借出点栈 · 库路径归一。
/// 对齐 S1「registry 精化」：任一失败即会让后续复跑的 owner 判定失真，故用并发进出钳制。
/// </summary>
[TestClass]
public class SqliteWriteOwnerRegistryTests
{
    private static string Cs()
    {
        var db = Path.Combine(Path.GetTempPath(), "reg_" + Guid.NewGuid().ToString("N") + ".db");
        return "Data Source=" + db + ";Version=3;";
    }

    [TestMethod]
    public void EnterExit_RoundTrip_LeavesEmpty()
    {
        var cs = Cs();
        var token = SqliteWriteOwnerRegistry.Enter(cs, "roundtrip");
        var d = SqliteWriteOwnerRegistry.Describe(cs);
        Assert.IsTrue(d.Contains("roundtrip"), "进入后应报持有者；got: " + d);
        Assert.IsTrue(d.Contains("borrow="), "短持描述应含单行首业务帧 borrow=；got: " + d);
        Assert.IsFalse(d.Contains("借出点"), "短持(<10s)应只给单行摘要、不展开借出点栈；got: " + d);
        Assert.IsFalse(d.Contains('\n'), "常规短持描述应整体单行；got: " + d);
        SqliteWriteOwnerRegistry.Exit(cs, token);
        Assert.AreEqual(string.Empty, SqliteWriteOwnerRegistry.Describe(cs), "退出后应无残留持有者");
    }

    [TestMethod]
    public void DescribeCompact_AlwaysSingleLine_NoStack()
    {
        var cs = Cs();
        var token = SqliteWriteOwnerRegistry.Enter(cs, "compact");
        try
        {
            var d = SqliteWriteOwnerRegistry.DescribeCompact(cs);
            Assert.IsTrue(d.Contains("compact|tid=") && d.Contains("|borrow="), "单行形态应含 owner|tid|borrow；got: " + d);
            Assert.IsFalse(d.Contains("借出点") && d.Contains('\n'), "DescribeCompact 应始终单行、不展开栈；got: " + d);
        }
        finally { SqliteWriteOwnerRegistry.Exit(cs, token); }
    }

    [TestMethod]
    public void DescribeFull_ExpandsBorrowStack()
    {
        var cs = Cs();
        var token = SqliteWriteOwnerRegistry.Enter(cs, "full");
        try
        {
            var d = SqliteWriteOwnerRegistry.DescribeFull(cs);
            Assert.IsTrue(d.Contains("full") && d.Contains("借出点") && d.Contains("at "),
                "DescribeFull 应含持有者与完整借出点栈；got: " + d);
        }
        finally { SqliteWriteOwnerRegistry.Exit(cs, token); }
    }

    [TestMethod]
    public void Describe_ExcludesSelf()
    {
        var cs = Cs();
        var token = SqliteWriteOwnerRegistry.Enter(cs, "self");
        Assert.AreEqual(string.Empty, SqliteWriteOwnerRegistry.Describe(cs, token), "排除自身后应空");
        SqliteWriteOwnerRegistry.Exit(cs, token);
    }

    [TestMethod]
    public void MultipleConcurrentHolders_RemovedIndividually()
    {
        var cs = Cs();
        var a = SqliteWriteOwnerRegistry.Enter(cs, "holder-A");
        var b = SqliteWriteOwnerRegistry.Enter(cs, "holder-B");
        var all = SqliteWriteOwnerRegistry.Describe(cs);
        Assert.IsTrue(all.Contains("holder-A") && all.Contains("holder-B"), "两持有者应并存；got: " + all);

        SqliteWriteOwnerRegistry.Exit(cs, a);
        var afterA = SqliteWriteOwnerRegistry.Describe(cs);
        Assert.IsTrue(!afterA.Contains("holder-A") && afterA.Contains("holder-B"), "移除 A 不应误删 B；got: " + afterA);

        SqliteWriteOwnerRegistry.Exit(cs, b);
        Assert.AreEqual(string.Empty, SqliteWriteOwnerRegistry.Describe(cs));
    }

    [TestMethod]
    public void Concurrent_EnterExit_LeavesNoPhantomHolder()
    {
        var cs = Cs();
        var tasks = Enumerable.Range(0, 8)
            .Select(worker => Task.Run(() =>
            {
                for (int i = 0; i < 800; i++)
                {
                    var t = SqliteWriteOwnerRegistry.Enter(cs, "worker-" + worker);
                    if ((i & 0x3F) == 0) Thread.Yield(); // 制造交叠窗口
                    SqliteWriteOwnerRegistry.Exit(cs, t);
                }
            }))
            .ToArray();
        Task.WaitAll(tasks);
        Assert.AreEqual(string.Empty, SqliteWriteOwnerRegistry.Describe(cs), "高并发进出后不应有虚报/残留持有者");
    }

    [TestMethod]
    public void DbPathNormalization_AliasesResolveSameHolder()
    {
        var db = Path.Combine(Path.GetTempPath(), "reg_" + Guid.NewGuid().ToString("N") + ".db");
        var cs1 = "Data Source=" + db + ";Version=3;";
        var cs2 = "data source = \"" + db + "\";Default Timeout=5000;"; // 大小写/空格/引号/多余参数
        var token = SqliteWriteOwnerRegistry.Enter(cs1, "normalize");
        SqliteWriteOwnerRegistry.Exit(cs2, token); // 归一后应命中同一库条目并注销
        Assert.AreEqual(string.Empty, SqliteWriteOwnerRegistry.Describe(cs1));
    }
}

