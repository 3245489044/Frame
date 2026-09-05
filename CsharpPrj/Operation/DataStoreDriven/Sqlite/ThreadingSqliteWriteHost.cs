using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Modules.Threading;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// 单库单写通道 host（Modules.Threading 版，S3/S4 语义，由原 DataManage <c>SqliteWriteChannel</c> 移植并 IoC 化）。
/// 原 <c>SqliteWriteChannel</c> 为 DataManage 静态宿主并 import Modules.Threading（低层反向进口并发框架）；
/// 现作为 DataManage 端口 <see cref="ISqliteWriteHost"/> 的实现由本模块（DataStoreDriven 组合根）提供并注入，
/// 使低层 DataManage 只依赖抽象端口、不再 import Threading（依赖倒置）。
/// <list type="bullet">
/// <item>每个归一化库文件配一个<b>专用单写 worker 线程 + 常驻写连接</b>（<see cref="SingleWriterActor{TResource}"/>）。</item>
/// <item>单条写 <see cref="RunWrite(string,Action{SqliteConnection},int,CancellationToken)"/>：提交到 worker（其线程 autocommit），有在途事务先等其结束。</item>
/// <item>事务：经 <see cref="GetOrCreate"/> 的 <see cref="ISqliteWriteEntry"/> 在 worker 上打开并保持到 Commit。</item>
/// </list>
/// 本 host 需与 SqlitePersistenceStore（事务）共用同一实例，使 SqliteChannelSession（节点写）与其共享同一 agent.db 写权。
/// </summary>
public sealed class ThreadingSqliteWriteHost : ISqliteWriteHost, IDisposable
{
    /// <summary>等在途事务结束的有界窗口（须 &gt; 单个合理事务时长）。超时即抛带「在途事务 owner/线程/打开时刻」的异常快速失败
    /// （按 execution_failure 归类）。internal 非 readonly 供故障注入测试收缩验证。</summary>
    internal static TimeSpan TxIdleWaitTimeout = TimeSpan.FromSeconds(10);

    /// <summary>进程级共享单写 host（等价旧 DataManage 全局静态 SqliteWriteChannel 语义）：不同装配路径
    /// （PersistenceProviderFactory 骨干 / DataStoreProviderFactory 节点通道）默认共用同一实例，
    /// 保证同一 agent.db 始终经同一单写写权。真正组合根可用 <see cref="ISqliteWriteHost"/> 注入自定义实例覆盖。</summary>
    public static ThreadingSqliteWriteHost Shared { get; } = new();

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>取/建库级写通道条目（端口视角）。</summary>
    public ISqliteWriteEntry GetOrCreate(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var key = SqliteWriteOwnerRegistry.DbPathOf(connectionString);
        return _entries.GetOrAdd(key, _ => CreateEntry(connectionString));
    }

    /// <summary>卸载指定库的写通道（停 worker、释放常驻写连接、移除条目）。测试/关闭用：释放后再次写入会重建。</summary>
    public void Release(string connectionString)
    {
        var key = SqliteWriteOwnerRegistry.DbPathOf(connectionString);
        if (_entries.TryRemove(key, out var entry)) entry.Actor.Stop();
    }

    /// <summary>当前已建条目库路径。</summary>
    public IEnumerable<string> ActiveLibraries() => _entries.Keys;

    private Entry GetEntry(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var key = SqliteWriteOwnerRegistry.DbPathOf(connectionString);
        return _entries.GetOrAdd(key, _ => CreateEntry(connectionString));
    }

    private static Entry CreateEntry(string connectionString)
    {
        var actor = new SingleWriterActor<SQLiteConnection>(
            create: () => OpenWriteConnection(connectionString),
            name: "sqlite-writer:" + Path.GetFileName(SqliteWriteOwnerRegistry.DbPathOf(connectionString)),
            dispose: conn => { try { conn.Dispose(); } catch { /* 释放失败静默 */ } });
        actor.Start();
        return new Entry { ConnectionString = connectionString, Actor = actor };
    }

    /// <summary>在 worker 线程上打开常驻写连接并初始化（WAL + busy_timeout，幂等语义 = 每次新建即设好）。</summary>
    private static SQLiteConnection OpenWriteConnection(string connectionString)
    {
        var conn = new SQLiteConnection(connectionString);
        conn.Open();
        using (var busy = conn.CreateCommand()) { busy.CommandText = "PRAGMA busy_timeout = 5000;"; busy.ExecuteNonQuery(); }
        using (var wal = conn.CreateCommand()) { wal.CommandText = "PRAGMA journal_mode = WAL;"; wal.ExecuteNonQuery(); }
        return conn;
    }

    /// <summary>等在途事务结束：worker 自身线程重入走内联；非 worker 才需等在途事务收尾（超时快速失败带 owner 自证）。</summary>
    private void WaitTxIdle(Entry entry)
    {
        if (entry.Actor.IsCurrentWorker) return;
        lock (entry.Sync)
        {
            if (!entry.TxOpen) return;
            var start = Stopwatch.GetTimestamp();
            while (entry.TxOpen)
            {
                var elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                if (elapsedMs >= TxIdleWaitTimeout.TotalMilliseconds) break;
                Monitor.Wait(entry.Sync, Math.Max(1, (int)(TxIdleWaitTimeout.TotalMilliseconds - elapsedMs)));
            }
            if (entry.TxOpen)
            {
                var openMs = entry.TxOpenedAtTicks == 0
                    ? 0.0
                    : (Stopwatch.GetTimestamp() - entry.TxOpenedAtTicks) * 1000.0 / Stopwatch.Frequency;
                throw new TimeoutException(
                    $"ThreadingSqliteWriteHost: 在途事务未在 {TxIdleWaitTimeout.TotalSeconds:0.0}s 内收尾（owner={entry.TxOwner ?? "<unknown>"}, " +
                    $"openThread={entry.TxOwnerThreadId}, openMs={openMs:0}），单条写等待超时——疑似事务门遗漏 MarkTxIdle，按 execution_failure 归类，不静默挂起。");
            }
        }
    }

    public void RunWrite(string connectionString, Action<SQLiteConnection> action,
        int timeoutMs = 5000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var entry = GetEntry(connectionString);
        WaitTxIdle(entry);
        entry.Actor.Execute(c => action(c), cancellationToken);
    }

    public T RunWrite<T>(string connectionString, Func<SQLiteConnection, T> action,
        int timeoutMs = 5000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var entry = GetEntry(connectionString);
        WaitTxIdle(entry);
        return entry.Actor.Execute(c => action(c), cancellationToken);
    }

    /// <summary>释放全部库级写通道（停 worker）。关闭用。</summary>
    public void Dispose()
    {
        foreach (var kv in _entries)
        {
            try { kv.Value.Actor.Stop(); } catch { /* 释放失败静默 */ }
        }
        _entries.Clear();
    }

    /// <summary>库级写通道条目（实现 DataManage 端口 <see cref="ISqliteWriteEntry"/>）：单写 actor + 事务门。</summary>
    internal sealed class Entry : ISqliteWriteEntry
    {
        public required string ConnectionString { get; init; }
        public required SingleWriterActor<SQLiteConnection> Actor { get; init; }
        /// <summary>事务门：TxOpen 置位期间，单条 autocommit 写须等待（避免混入在途事务）。</summary>
        public readonly object Sync = new();
        public bool TxOpen;
        // ---- 在途事务 owner 自证（防永久等待诊断，S4）：MarkTxOpen 时登记、MarkTxIdle 清除；WaitTxIdle 超时报告复用。----
        public string? TxOwner;          // 打开者标签（缺省登记当前线程名）
        public int TxOwnerThreadId;      // 打开线程
        public long TxOpenedAtTicks;     // 打开时刻（Stopwatch 时间戳）

        public bool IsCurrentWorker => Actor.IsCurrentWorker;

        public void Execute(Action<SQLiteConnection> action, CancellationToken cancellationToken = default)
            => Actor.Execute(action, cancellationToken);

        public T Execute<T>(Func<SQLiteConnection, T> action, CancellationToken cancellationToken = default)
            => Actor.Execute(action, cancellationToken);

        public Task ExecuteAsync(Action<SQLiteConnection> action, CancellationToken cancellationToken = default)
            => Actor.ExecuteAsync(action, cancellationToken);

        public void MarkTxOpen(string? owner = null)
        {
            lock (Sync)
            {
                TxOpen = true;
                TxOwner = string.IsNullOrWhiteSpace(owner) ? $"tx@{Environment.CurrentManagedThreadId}" : owner;
                TxOwnerThreadId = Environment.CurrentManagedThreadId;
                TxOpenedAtTicks = Stopwatch.GetTimestamp();
            }
        }

        public void MarkTxIdle()
        {
            lock (Sync)
            {
                TxOpen = false;
                TxOwner = null;
                TxOwnerThreadId = 0;
                TxOpenedAtTicks = 0;
                Monitor.PulseAll(Sync);
            }
        }
    }
}
