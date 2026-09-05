#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataManage.Abstractions;
using DataManage.Abstractions.Models;

namespace DataManage.Data.Graph
{
    public class GraphTransaction : IGraphTransaction
    {
        private readonly List<TransactionOperation> _operations = new();
        private readonly IGraphStorage _storage;
        private bool _isCommitted;
        private bool _isRolledBack;
        private readonly object _lock = new();

        public GraphTransaction(IGraphStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public bool IsCommitted => _isCommitted;
        public bool IsRolledBack => _isRolledBack;

        public IReadOnlyList<TransactionOperation> GetOperations()
        {
            lock (_lock)
            {
                return _operations.AsReadOnly();
            }
        }

        public async Task StoreResourceAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            Dictionary<string, object> metadata,
            string? symbol = null,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _operations.Add(new TransactionOperation
                {
                    OperationType = TransactionOperationType.Store,
                    ResourceType = resourceId.ResourceType,
                    ResourceId = resourceId.Id,
                    Metadata = metadata,
                    Symbol = symbol,
                    Timestamp = DateTime.UtcNow
                });
            }

            await Task.CompletedTask;
        }

        public async Task DeleteResourceAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            CancellationToken cancellationToken = default)
        {
            var existingResource = await _storage.GetResourceAsync(resourceId);

            lock (_lock)
            {
                _operations.Add(new TransactionOperation
                {
                    OperationType = TransactionOperationType.Delete,
                    ResourceType = resourceId.ResourceType,
                    ResourceId = resourceId.Id,
                    Metadata = existingResource?.Metadata,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        public async Task UpdateResourceAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            Dictionary<string, object> metadata,
            CancellationToken cancellationToken = default)
        {
            var existingResource = await _storage.GetResourceAsync(resourceId);

            lock (_lock)
            {
                _operations.Add(new TransactionOperation
                {
                    OperationType = TransactionOperationType.Update,
                    ResourceType = resourceId.ResourceType,
                    ResourceId = resourceId.Id,
                    Metadata = existingResource?.Metadata,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (_isCommitted || _isRolledBack)
                    throw new InvalidOperationException("Transaction has already been committed or rolled back.");

                _isCommitted = true;
            }

            List<TransactionOperation> operationsToCommit;
            lock (_lock)
            {
                operationsToCommit = new List<TransactionOperation>(_operations);
            }

            foreach (var op in operationsToCommit)
            {
                var resourceId = new UniversalWorkflowFramework.Core.Models.ResourceIdentifier { ResourceType = op.ResourceType, Id = op.ResourceId };
                
                switch (op.OperationType)
                {
                    case TransactionOperationType.Store:
                        await _storage.StoreResourceAsync(resourceId, op.Metadata!, op.Symbol);
                        break;
                    case TransactionOperationType.Delete:
                        await _storage.DeleteResourceAsync(resourceId);
                        break;
                    case TransactionOperationType.Update:
                        await _storage.UpdateResourceAsync(resourceId, op.Metadata!);
                        break;
                }
            }
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (_isCommitted)
                    throw new InvalidOperationException("Cannot rollback a committed transaction.");

                if (_isRolledBack)
                    return;

                _isRolledBack = true;
            }

            await Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_isCommitted && !_isRolledBack && _operations.Count > 0)
            {
                await RollbackAsync();
            }
        }
    }

    public static class GraphTransactionFactory
    {
        public static IGraphTransaction Create(IGraphStorage storage)
        {
            return new GraphTransaction(storage);
        }
    }
}