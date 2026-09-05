using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Threading;

namespace UniversalWorkflowFramework.StorageFramework.Sqlite;

/// <summary>
/// 库级单写通道端口（IoC 倒置）：DataManage（Plate）只依赖本抽象端口，不依赖 Modules.Threading 具体实现。
/// 基于 Modules.Threading（SingleWriterActor）的实现由上层 DataStoreDriven 组合根提供并注入，
/// 使低层不再"反向进口"并发框架，依赖方向指向抽象。
/// </summary>
public interface ISqliteWriteHost
{
    /// <summary>取/建指定库的写通道条目（首建即在 worker 线程创建并打开常驻写连接，开启 WAL + busy_timeout）。</summary>
    ISqliteWriteEntry GetOrCreate(string connectionString);

    /// <summary>在指定库的单写连接上执行一个写动作（提交给该库 worker 线程串行执行；异常/取消安全返回）。</summary>
    void RunWrite(string connectionString, Action<SQLiteConnection> action,
        int timeoutMs = 5000, CancellationToken cancellationToken = default);

    /// <summary>在指定库的单写连接上执行一个写动作并返回结果。</summary>
    T RunWrite<T>(string connectionString, Func<SQLiteConnection, T> action,
        int timeoutMs = 5000, CancellationToken cancellationToken = default);

    /// <summary>卸载指定库的写通道（停 worker、释放常驻写连接、移除条目）。释放后再次写入会重建。</summary>
    void Release(string connectionString);

    /// <summary>当前已建立的库级写通道条目路径（供关闭/诊断遍历；可能为空集合）。</summary>
    IEnumerable<string> ActiveLibraries();
}

/// <summary>库级写通道条目（端口侧视图，屏蔽 SingleWriterActor 细节）。仅供 SqliteTransaction 在 worker 上执行并维护事务门。</summary>
public interface ISqliteWriteEntry
{
    /// <summary>当前调用是否位于该库写 worker 线程（重入内联判定）。</summary>
    bool IsCurrentWorker { get; }

    /// <summary>在 worker 线程上同步执行写动作（worker 自身重入则内联；异常/取消安全返回）。</summary>
    void Execute(Action<SQLiteConnection> action, CancellationToken cancellationToken = default);

    /// <summary>在 worker 线程上同步执行写动作并返回结果（worker 自身重入则内联）。</summary>
    T Execute<T>(Func<SQLiteConnection, T> action, CancellationToken cancellationToken = default);

    /// <summary>异步提交同步体到 worker 并 await 完成（调用方不阻塞；入队的工作体在 worker 上同步跑完）。</summary>
    Task ExecuteAsync(Action<SQLiteConnection> action, CancellationToken cancellationToken = default);

    /// <summary>置位「该库有在途事务」，期间单条写须等待；登记打开者（owner 自证，供超时诊断）。</summary>
    void MarkTxOpen(string? owner = null);

    /// <summary>清除在途事务标记并唤醒等待的单条写。</summary>
    void MarkTxIdle();
}
