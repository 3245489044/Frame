using System;
using System.Threading;
using System.Threading.Tasks;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.StorageFramework.Core;

/// <summary>
/// 无事务后端（LiteDB/Graph/Local 等非原生 SQL 事务后端）的占位事务。
/// 提交/回滚为空操作；<see cref="Commands"/> 因无 SQL 命令概念而抛 <see cref="NotSupportedException"/>。
/// 原生事务是 Sqlite 的强项；其余后端的原生事务化归 P4 增强。
/// </summary>
public sealed class NoopTransaction : ITransaction
{
    public static readonly NoopTransaction Instance = new();

    /// <summary>无事务后端的共享事务工厂（Graph/Local/LiteDB 复用，消除三处重复 lambda）。</summary>
    public static readonly Func<CancellationToken, Task<ITransaction>> Factory =
        static _ => Task.FromResult<ITransaction>(Instance);

    private NoopTransaction() { }

    public IDbCommandExecutor Commands =>
        throw new NotSupportedException("该后端不支持 SQL 命令执行；事务内操作请使用后端自带 API。");

    public Task CommitAsync() => Task.CompletedTask;

    public Task RollbackAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
