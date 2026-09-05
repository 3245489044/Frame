using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Threading;
using System.Threading.Tasks;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.StorageFramework.Sqlite;

/// <summary>
/// SQLite 事务（S3 重构版）——worker 会话型，<b>同步构造</b>（在调用方 ExecutionContext 内登记环境事务，保证
/// await 返回后调用方事务内后续写仍路由到本事务；若在异步方法内 Enter，AsyncLocal 变更不传回调用方续延上下文，
/// 事务内写会落到单写门 → WaitTxIdle 自锁）。原生事务不在构造时打开，而在<b>首次命令/提交时</b>于该库单写 worker
/// 线程上懒开并保持到 Commit——期间其它单条写被事务门阻塞（不混入无关事务），杜绝跨线程持连接与门超时饿死。
/// </summary>
public sealed class SqliteTransaction : ITransaction
{
    private readonly ISqliteWriteEntry _entry;
    private SQLiteTransaction? _native;      // 原生事务；仅 worker 线程创建/读取
    private IDisposable? _ambientScope;
    private bool _finished;
    private readonly object _stateLock = new();

    public IDbCommandExecutor Commands { get; }

    /// <summary>同步构造：登记本库写权在途 + 在<b>调用方 ExecutionContext</b> 登记环境事务（见类注释）。</summary>
    /// <param name="host">注入的单写通道 host（由上层组合根提供，Modules.Threading 版单写实现）。</param>
    /// <param name="connectionString">目标库连接串。</param>
    /// <param name="ownerLabel">可选打开者标签（owner 自证诊断，WaitTxIdle 超时报告复用）；缺省登记当前线程。</param>
    public SqliteTransaction(ISqliteWriteHost host, string connectionString, string? ownerLabel = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("连接串不能为空。", nameof(connectionString));
        _entry = host.GetOrCreate(connectionString);
        _entry.MarkTxOpen(ownerLabel);
        _ambientScope = SqliteAmbientTransaction.Enter(this);
        Commands = new WorkerSession(this);
    }

    /// <summary>事务已结束（提交/回滚/释放）。供环境事务路由回退判定：AsyncLocal 可能因 async DisposeAsync 未传播而残留。</summary>
    internal bool IsFinished
    {
        get { lock (_stateLock) { return _finished; } }
    }

    public Task CommitAsync() => FinishAsync(commit: true);

    public Task RollbackAsync() => FinishAsync(commit: false);

    public async ValueTask DisposeAsync()
    {
        if (!IsFinished) await FinishAsync(commit: false).ConfigureAwait(false);
    }

    private Task FinishAsync(bool commit)
    {
        if (IsFinished) return Task.CompletedTask;
        var tx = this;
        return _entry.ExecuteAsync(conn =>
        {
            try
            {
                if (tx._native is not null)
                {
                    var native = tx._native;
                    try
                    {
                        if (commit) native.Commit(); else native.Rollback();
                    }
                    finally
                    {
                        // native 收尾成败都释放原生事务对象，避免单写连接残留悬挂事务（worker 后续写仍干净）。
                        native.Dispose();
                    }
                }
            }
            finally
            {
                // 门释放与事务生命周期强耦合于本 worker 收尾（S4 治本）：无论 native Commit/Rollback/Dispose 是否抛错，
                // 一旦开始 Finish 必置结束态 + MarkTxIdle —— 杜绝异常/取消路径遗留 TxOpen，使后续单条写永久等待。
                tx._native = null;
                tx.Complete();
                _entry.MarkTxIdle();
            }
        });
    }

    private void Complete()
    {
        lock (_stateLock)
        {
            var scope = _ambientScope;
            _ambientScope = null;
            scope?.Dispose();
            _finished = true;
        }
    }

    /// <summary>worker 路由执行器：每条命令提交到该库单写 worker；首次执行在该 worker 上懒开原生事务。</summary>
    private sealed class WorkerSession : IDbCommandExecutor
    {
        private readonly SqliteTransaction _tx;

        public WorkerSession(SqliteTransaction tx) => _tx = tx;

        private T Run<T>(Func<SQLiteConnection, SQLiteTransaction, T> action)
            => _tx._entry.Execute(conn =>
            {
                if (_tx._native is null) _tx._native = conn.BeginTransaction(); // worker 上懒开
                return action(conn, _tx._native);
            });

        public int ExecuteNonQuery(string sql, IDictionary<string, object>? parameters = null)
            => Run((conn, native) =>
            {
                using var cmd = CreateCommand(conn, native, sql, parameters);
                return cmd.ExecuteNonQuery();
            });

        public long ExecuteInsertReturningId(string sql, IDictionary<string, object>? parameters = null)
            => Run((conn, native) =>
            {
                using var cmd = CreateCommand(conn, native, sql, parameters);
                cmd.ExecuteNonQuery();
                return cmd.Connection.LastInsertRowId;
            });

        public object? ExecuteScalar(string sql, IDictionary<string, object>? parameters = null)
            => Run((conn, native) =>
            {
                using var cmd = CreateCommand(conn, native, sql, parameters);
                var v = cmd.ExecuteScalar();
                return v is DBNull ? null : v;
            });

        public IReadOnlyList<IReadOnlyDictionary<string, object>> ExecuteQuery(string sql, IDictionary<string, object>? parameters = null)
            => Run((conn, native) =>
            {
                var result = new List<IReadOnlyDictionary<string, object>>();
                using var cmd = CreateCommand(conn, native, sql, parameters);
                using var r = cmd.ExecuteReader();
                while (r.Read()) result.Add(SqliteRows.ReadRow(r));
                return result;
            });

        private static SQLiteCommand CreateCommand(SQLiteConnection conn, SQLiteTransaction native,
            string sql, IDictionary<string, object>? parameters)
        {
            var cmd = new SQLiteCommand(sql, conn, native);
            if (parameters != null)
                foreach (var p in parameters)
                    cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
            return cmd;
        }
    }
}
