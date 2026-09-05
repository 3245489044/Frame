#nullable enable

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataManage.Abstractions;

namespace DataManage.Data.Graph
{
    public class GraphTransactionManager : IGraphTransactionManager
    {
        private readonly IGraphStorage _storage;
        private IGraphTransaction? _currentTransaction;
        private readonly object _lock = new();

        public GraphTransactionManager(IGraphStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public bool HasActiveTransaction => _currentTransaction != null &&
            !((GraphTransaction)_currentTransaction).IsCommitted &&
            !((GraphTransaction)_currentTransaction).IsRolledBack;

        public IGraphTransaction? CurrentTransaction => _currentTransaction;

        public IGraphTransaction BeginTransaction()
        {
            lock (_lock)
            {
                if (HasActiveTransaction)
                    throw new InvalidOperationException("A transaction is already in progress.");

                _currentTransaction = new GraphTransaction(_storage);
                return _currentTransaction;
            }
        }

        public Task<IGraphTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BeginTransaction());
        }

        public Task<IGraphTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BeginTransaction());
        }
    }

    public static class GraphTransactionManagerFactory
    {
        public static IGraphTransactionManager Create(IGraphStorage storage)
        {
            return new GraphTransactionManager(storage);
        }
    }
}