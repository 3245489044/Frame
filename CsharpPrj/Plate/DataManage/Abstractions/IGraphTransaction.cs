#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataManage.Abstractions.Models;

namespace DataManage.Abstractions
{
    public interface IGraphTransaction : IAsyncDisposable
    {
        Task CommitAsync(CancellationToken cancellationToken = default);

        Task RollbackAsync(CancellationToken cancellationToken = default);

        Task StoreResourceAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            Dictionary<string, object> metadata,
            string? symbol = null,
            CancellationToken cancellationToken = default);

        Task DeleteResourceAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            CancellationToken cancellationToken = default);

        Task UpdateResourceAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            Dictionary<string, object> metadata,
            CancellationToken cancellationToken = default);

        bool IsCommitted { get; }

        bool IsRolledBack { get; }

        IReadOnlyList<TransactionOperation> GetOperations();
    }

    public interface IGraphTransactionManager
    {
        IGraphTransaction BeginTransaction();

        Task<IGraphTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

        Task<IGraphTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default);

        bool HasActiveTransaction { get; }

        IGraphTransaction? CurrentTransaction { get; }
    }
}