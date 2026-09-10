using System;
using System.Threading.Tasks;

namespace UniversalWorkflowFramework.StorageFramework.Abstractions;

/// <summary>数据库原生事务端口。事务内命令经 <see cref="Commands"/> 在同一连接/事务上执行；提交或回滚后（或未提交释放）事务结束。</summary>
public interface ITransaction : IAsyncDisposable
{
    IDbCommandExecutor Commands { get; }

    Task CommitAsync();

    Task RollbackAsync();
}
