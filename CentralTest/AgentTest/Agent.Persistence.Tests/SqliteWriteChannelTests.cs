using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Agent.Persistence.Tests;

/// <summary>
/// 单库单写通道 host（ThreadingSqliteWriteHost，Modules.Threading 版，原 SqliteWriteChannel 移植）S3 重构回归：
/// 写走单写 actor（worker 串行、无门超时）；事务为 worker 会话型（Begin/命令/Commit 同一 worker 连接原子完成，
/// 期间并发单条写等待至提交，不混入、不 5s 超时死信）。
/// 覆盖：并发单条写全成功 · 事务 Commit 落库 / Rollback 丢弃 · 事务期间并发写阻塞至提交后成功。
/// </summary>
[TestClass]
public class SqliteWriteChannelTests
{
    private string _dir = string.Empty;
    private string _cs = string.Empty;
    private ThreadingSqliteWriteHost _host = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "SwaChannelTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        var db = Path.Combine(_dir, "agent.db");
        _cs = "Data Source=" + db + ";Version=3;Default Timeout=5000;";
        _host = new ThreadingSqliteWriteHost();
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { _host.Release(_cs); } catch { /* 忽略 */ }
        try { _host.Dispose(); } catch { /* 忽略 */ }
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* 忽略清理异常 */ }
    }

    [TestMethod]
    public async Task ConcurrentRunWrites_AllSucceed_NoTimeout()
    {
        EnsureTable();

        var t1 = Task.Run(() => Insert("a"));
        var t2 = Task.Run(() => Insert("b"));
        await Task.WhenAll(t1, t2);

        Assert.AreEqual(2, CountRows(), "并发单条写应全部成功、无 5s 借出超时死信。");
    }

    [TestMethod]
    public async Task Transaction_CommitPersists_RollbackDiscards()
    {
        EnsureTable();

        // Commit 路径
        await using (var tx = new SqliteTransaction(_host, _cs))
        {
            tx.Commands.ExecuteNonQuery("INSERT INTO t(v) VALUES ('c1');");
            await tx.CommitAsync();
        }
        Assert.AreEqual(1, CountRows(), "Commit 应落库。");

        // Rollback 路径
        await using (var tx = new SqliteTransaction(_host, _cs))
        {
            tx.Commands.ExecuteNonQuery("INSERT INTO t(v) VALUES ('c2');");
            await tx.RollbackAsync();
        }
        Assert.AreEqual(1, CountRows(), "Rollback 应丢弃未提交写入。");
    }

    [TestMethod]
    public async Task ConcurrentRunWrite_DuringOpenTransaction_WaitsUntilCommit()
    {
        EnsureTable();

        await using var tx = new SqliteTransaction(_host, _cs);
        tx.Commands.ExecuteNonQuery("INSERT INTO t(v) VALUES ('in-tx');");

        // 事务在途期间，另一线程发起单条写：应阻塞等待（公平串行），而非 5s 超时失败/混入事务。
        var blocker = Task.Run(() => Insert("single"));

        await Task.Delay(120);
        Assert.IsFalse(blocker.IsCompleted, "事务在途期间并发单条写应被阻塞（等待提交），不得提前执行/超时。");

        await tx.CommitAsync();
        await blocker; // 提交后放行并成功

        Assert.AreEqual(2, CountRows(), "提交后并发写成功，事务外写入独立计数。");
    }

    [TestMethod]
    public void OpenTransactionLeaked_ConcurrentRunWrite_FailsBoundedWithOwner_NotPermanentHang()
    {
        // S4 步骤2/3 兜底：即便事务门被遗留（TxOpen 永久为 true），单条写也须「有界失败 + owner 自证」，而非无限等待静默挂起。
        EnsureTable();

        // SqliteTransaction 仅实现 IAsyncDisposable；此处手工收尾（finally 内 RollbackAsync 即释放门），故不用 using 语法。
        var leaked = new SqliteTransaction(_host, _cs, ownerLabel: "fault-inject-leak");
        leaked.Commands.ExecuteNonQuery("INSERT INTO t(v) VALUES ('leak');"); // worker 上懒开原生事务，门置位

        var original = GetTxIdleTimeout();
        SetTxIdleTimeout(TimeSpan.FromMilliseconds(500)); // 收缩窗口验证有界路径，避免 10s 测试耗时
        try
        {
            var sw = Stopwatch.StartNew();
            Exception? thrown = null;
            try { Insert("blocked"); }
            catch (Exception ex) { thrown = ex; }
            sw.Stop();

            Assert.IsNotNull(thrown, "在途事务被遗留时，单条写应在有界窗口内抛错而非永久挂起。");
            Assert.IsInstanceOfType(thrown, typeof(TimeoutException),
                $"有界等待超时应抛带 self-evidence 的 TimeoutException，实际 {thrown!.GetType().Name}: {thrown.Message}");
            Assert.IsTrue(thrown.Message.Contains("fault-inject-leak", StringComparison.Ordinal)
                          && thrown.Message.Contains("ThreadingSqliteWriteHost", StringComparison.Ordinal),
                $"异常应含 owner 自证（fault-inject-leak），实际: {thrown.Message}");
            Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(8),
                $"应在有界窗口内失败（实际 {sw.Elapsed.TotalSeconds:0.0}s），不得静默挂起。");
        }
        finally
        {
            SetTxIdleTimeout(original);
            // 收尾清理：Rollback + MarkTxIdle（门此刻应正常释放）。
            leaked.RollbackAsync().GetAwaiter().GetResult();
        }
    }

    [TestMethod]
    public async Task TransactionBodyAborts_DisposeReleasesGate_NextRunWriteSucceeds()
    {
        // S4 步骤1/治本 + 既有 await-using 契约：事务块内异常/取消（未 Commit）经 DisposeAsync 收尾释放门，
        // 后续单条写不被永久阻塞（若门遗漏 MarkTxIdle，下面 Insert 将挂死 → 测试超时兜底暴露回归）。
        EnsureTable();

        try
        {
            await using (var tx = new SqliteTransaction(_host, _cs))
            {
                tx.Commands.ExecuteNonQuery("INSERT INTO t(v) VALUES ('abort');");
                throw new InvalidOperationException("注入事务体异常：未 Commit 即中止"); // 永不达 using 末尾，故无后续代码
            }
        }
        catch (InvalidOperationException)
        {
            // 预期：事务体抛错（await using finally 走 DisposeAsync → FinishAsync(false) → MarkTxIdle）
        }

        Insert("after");
        Assert.AreEqual(1, CountRows(), "注入事务写入应被回滚丢弃，且事务门已释放、后续单条写即时成功。");
    }

    private static TimeSpan GetTxIdleTimeout()
        => (TimeSpan)(typeof(ThreadingSqliteWriteHost)
            .GetField("TxIdleWaitTimeout", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("字段 TxIdleWaitTimeout 不存在")).GetValue(null)!;

    private static void SetTxIdleTimeout(TimeSpan ts)
        => (typeof(ThreadingSqliteWriteHost)
            .GetField("TxIdleWaitTimeout", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("字段 TxIdleWaitTimeout 不存在")).SetValue(null, ts);

    private void EnsureTable()
        => _host.RunWrite(_cs, conn =>
        {
            using var c = conn.CreateCommand();
            c.CommandText = "CREATE TABLE IF NOT EXISTS t(id INTEGER PRIMARY KEY, v TEXT);";
            c.ExecuteNonQuery();
        });

    private void Insert(string v)
        => _host.RunWrite(_cs, conn =>
        {
            using var c = conn.CreateCommand();
            c.CommandText = "INSERT INTO t(v) VALUES (@v);";
            c.Parameters.AddWithValue("@v", v);
            c.ExecuteNonQuery();
        });

    private int CountRows()
        => _host.RunWrite(_cs, conn =>
        {
            using var c = conn.CreateCommand();
            c.CommandText = "SELECT COUNT(*) FROM t;";
            return System.Convert.ToInt32(c.ExecuteScalar());
        });
}
