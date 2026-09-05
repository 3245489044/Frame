using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 持久化端口（IoC 反转：业务只依赖此抽象，不依赖具体后端）。
/// 提供统一初始化、checkpoint/快照访问、资源/引用访问与数据库原生事务。
/// </summary>
public interface IPersistenceStore : IAsyncDisposable
{
    /// <summary>初始化后端（建库/建表/连接预热）；<c>AutoCreateSchema</c> 时由各后端 Schema 初始化器建表。</summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>checkpoint/快照访问（LangGraph 兼容端口）。</summary>
    ICheckpointStore Checkpoints { get; }

    /// <summary>Agent 状态快照访问（AxiomContext 序列化；崩溃恢复 / 跨会话恢复）。</summary>
    ISnapshotStore Snapshots { get; }

    /// <summary>开启数据库原生事务。事务内命令经 <see cref="ITransaction.Commands"/> 执行。</summary>
    Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default);
}
