using System;
using System.Threading;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.StorageFramework.Sqlite;

/// <summary>
/// SQLite 事务环境作用域（AsyncLocal）。<see cref="SqliteTransaction"/> 开启时经 <see cref="Enter"/> 登记为当前环境事务，
/// 使配置存储 等"自动提交"写路径经 <see cref="Resolve"/> 路由到同一事务连接，
/// 从而让 <see cref="ITransaction"/>（BeginTransactionAsync）真正覆盖三区 / 配置写（修复"事务不覆盖写"问题）。
/// 事务提交 / 回滚 / 释放后自动清除；非 Sqlite 后端与未开启事务时不受影响（回落默认执行器）。
/// </summary>
internal static class SqliteAmbientTransaction
{
    private static readonly AsyncLocal<ITransaction?> _current = new();

    /// <summary>当前环境事务；未开启为 null。</summary>
    public static ITransaction? Current => _current.Value;

    /// <summary>
    /// 把事务登记为当前环境事务并返回作用域。作用域释放时，仅当当前环境仍是该事务才清除（避免误清外层事务）。
    /// 线程亲和：AsyncLocal 值随异步流程流动，故 <c>await using</c> 的 Dispose 在同一异步上下文清除登记。
    /// </summary>
    public static IDisposable Enter(ITransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var prior = _current.Value;
        _current.Value = transaction;
        return new Scope(transaction, prior);
    }

    /// <summary>若当前环境有事务，返回其命令执行器（写路由到事务连接）；否则返回默认执行器。
    /// 事务已结束（提交/回滚/释放）即使 AsyncLocal 残留也回退默认执行器——async DisposeAsync 的 AsyncLocal 修改
    /// 不传播到 await using 块外的调用方上下文，须在此兜底，避免路由到已释放的事务连接。</summary>
    public static IDbCommandExecutor Resolve(IDbCommandExecutor defaultExecutor)
    {
        var tx = _current.Value;
        if (tx is SqliteTransaction st && st.IsFinished)
            return defaultExecutor;
        return tx?.Commands ?? defaultExecutor;
    }

    private sealed class Scope : IDisposable
    {
        private readonly ITransaction _transaction;
        private readonly ITransaction? _prior;
        private bool _disposed;

        public Scope(ITransaction transaction, ITransaction? prior)
        {
            _transaction = transaction;
            _prior = prior;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(_current.Value, _transaction))
                _current.Value = _prior;
        }
    }
}
