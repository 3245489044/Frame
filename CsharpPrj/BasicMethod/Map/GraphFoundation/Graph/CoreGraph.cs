
#nullable enable

using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using GraphFoundation.Abstractions;
using GraphFoundation.Storage;

namespace GraphFoundation.Core
{
    public class CoreGraph<TKey, TNodeData, TEdgeData> : ICoreGraph<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly IGraphStorage<TKey, TNodeData, TEdgeData> _storage;

        private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
        private bool _disposed;
        private LockedTransaction? _activeTransaction;

        private readonly Subject<GraphEvent> _eventSubject = new();
        private bool _eventCompleted;

        public IObservable<GraphEvent> Events => _eventSubject;

        internal IErrorHandler? ErrorHandler { get; }

        internal IGraphStorage<TKey, TNodeData, TEdgeData> Storage => _storage;

        public CoreGraph(IGraphStorage<TKey, TNodeData, TEdgeData>? storage = null,
        IErrorHandler? errorHandler = null)
        {
        _storage = storage ?? new InMemoryGraphStorage<TKey, TNodeData, TEdgeData>();
        ErrorHandler = errorHandler;
        }

        public bool GetEdgeIsDirected(string edgeId)
        {
            using (ReadLock())
            {
                if (_storage.TryGetEdge(edgeId, out var snapshot))
                    return snapshot.IsDirected;
                throw new KeyNotFoundException($"Edge with id '{edgeId}' not found.");
            }
        }

        public (TKey Source, TKey Target, TEdgeData? Data, double Weight)? TryGetEdgeDetails(string edgeId)
        {
            using (ReadLock())
            {
                if (_storage.TryGetEdge(edgeId, out var snapshot))
                    return (snapshot.Source, snapshot.Target, snapshot.Data, snapshot.Weight);
                return null;
            }
        }


        public TNodeData? GetNodeData(TKey key)
        {
            using (ReadLock())
            {
                return _storage.TryGetNode(key, out var data, out _) ? data : null;
            }
        }

        public string? GetNodeSymbol(TKey key)
        {
            using (ReadLock())
            {
                return _storage.TryGetNode(key, out _, out var symbol) ? symbol : null;
            }
        }

        public TEdgeData? GetEdgeData(string edgeId)
        {
            using (ReadLock())
            {
                return _storage.TryGetEdge(edgeId, out var snapshot) ? snapshot.Data : default;
            }
        }

        public double GetEdgeWeight(string edgeId)
        {
            using (ReadLock())
            {
                if (_storage.TryGetEdge(edgeId, out var snapshot))
                    return snapshot.Weight;
                throw new KeyNotFoundException($"Edge with id '{edgeId}' not found.");
            }
        }

        public TKey GetEdgeSource(string edgeId)
        {
            using (ReadLock())
            {
                if (_storage.TryGetEdge(edgeId, out var snapshot))
                    return snapshot.Source;
                throw new KeyNotFoundException($"Edge with id '{edgeId}' not found.");
            }
        }

        public TKey GetEdgeTarget(string edgeId)
        {
            using (ReadLock())
            {
                if (_storage.TryGetEdge(edgeId, out var snapshot))
                    return snapshot.Target;
                throw new KeyNotFoundException($"Edge with id '{edgeId}' not found.");
            }
        }

        public IReadOnlyList<TKey> GetAllNodeKeys()
        {
            using (ReadLock())
            {
                return _storage.GetAllNodeKeys().ToList();
            }
        }

        public IReadOnlyList<string> GetAllEdgeIds()
        {
            using (ReadLock())
            {
                return _storage.GetAllEdgeIds().ToList();
            }
        }

        public IReadOnlyList<(TKey Source, TKey Target, string EdgeId, bool IsDirected, double Weight, TEdgeData Data)> GetOutgoingEdges(TKey source)
        {
            using (ReadLock())
            {
                return _storage.GetOutgoingEdges(source)
                    .Select(s => (s.Source, s.Target, s.EdgeId, s.IsDirected, s.Weight, s.Data!))
                    .ToList();
            }
        }

        public IReadOnlyList<(TKey Source, TKey Target, string EdgeId, bool IsDirected, double Weight, TEdgeData Data)> GetIncomingEdges(TKey target)
        {
            using (ReadLock())
            {
                return _storage.GetIncomingEdges(target)
                    .Select(s => (s.Source, s.Target, s.EdgeId, s.IsDirected, s.Weight, s.Data!))
                    .ToList();
            }
        }

        public bool ContainsNode(TKey key)
        {
            using (ReadLock())
            {
                return _storage.ContainsNode(key);
            }
        }

        public bool ContainsEdge(string edgeId)
        {
            using (ReadLock())
            {
                return _storage.ContainsEdge(edgeId);
            }
        }

        public IReadOnlyList<TKey> GetNeighbors(TKey key)
        {
            using (ReadLock())
            {
                return _storage.GetNeighbors(key).ToList();
            }
        }


        public ITransaction BeginTransaction()
        {
            _lock.EnterWriteLock();
            var tx = new LockedTransaction(this, ErrorHandler);
            _activeTransaction = tx;
            return tx;
        }

        public void AddNode(TKey key, TNodeData data, string? symbol = null)
        {
            using var tx = BeginTransaction();
            tx.Enqueue(new Commands.AddNodeCommand<TKey, TNodeData, TEdgeData>(key, data, symbol));
            tx.Commit();
        }

        public void RemoveNode(TKey key)
        {
            using var tx = BeginTransaction();
            tx.Enqueue(new Commands.RemoveNodeCommand<TKey, TNodeData, TEdgeData>(key));
            tx.Commit();
        }

        public void AddEdge(string edgeId, TKey source, TKey target, bool isDirected, TEdgeData? data, double weight = 1.0)
        {
            using var tx = BeginTransaction();
            tx.Enqueue(new Commands.AddEdgeCommand<TKey, TNodeData, TEdgeData>(edgeId, source, target, isDirected, data, weight));
            tx.Commit();
        }

        public void RemoveEdge(string edgeId)
        {
            using var tx = BeginTransaction();
            tx.Enqueue(new Commands.RemoveEdgeCommand<TKey, TNodeData, TEdgeData>(edgeId));
            tx.Commit();
        }

        public void UpdateNode(TKey key, TNodeData newData, string? newSymbol = null)
        {
            using var tx = BeginTransaction();
            tx.Enqueue(new Commands.UpdateNodeCommand<TKey, TNodeData, TEdgeData>(key, newData, newSymbol));
            tx.Commit();
        }


        internal void PublishEvent(GraphEvent evt)
        {
            lock (_eventSubject)
            {
                if (_eventCompleted) return;
                _eventSubject.OnNext(evt);
            }
        }

        internal void PublishEvents(IEnumerable<GraphEvent> events)
        {
            foreach (var evt in events)
                PublishEvent(evt);
        }

        internal void ReleaseWriteLock()
        {
            if (_lock.IsWriteLockHeld)
                _lock.ExitWriteLock();
        }


        private ReadLockGuard ReadLock() => new(_lock);

        private readonly struct ReadLockGuard : IDisposable
        {
            private readonly ReaderWriterLockSlim _lock;
            public ReadLockGuard(ReaderWriterLockSlim rwLock)
            {
                _lock = rwLock;
                _lock.EnterReadLock();
            }
            public void Dispose() => _lock.ExitReadLock();
        }


        private class LockedTransaction : ITransaction
        {
            private readonly CoreGraph<TKey, TNodeData, TEdgeData> _parent;
            private readonly IErrorHandler? _errorHandler;
            private readonly List<ICommand> _commands = new();
            private readonly List<Action> _commitCallbacks = new();
            private bool _committed, _disposed;

            public LockedTransaction(CoreGraph<TKey, TNodeData, TEdgeData> parent, IErrorHandler? errorHandler)
            {
                _parent = parent;
                _errorHandler = errorHandler;
            }

            public bool IsActive => !_committed && !_disposed;

            public void Enqueue(ICommand command)
            {
                if (_committed || _disposed)
                    throw new InvalidOperationException("Cannot enqueue command to a transaction that is already committed or disposed.");
                _commands.Add(command);
            }

            public void OnCommit(Action callback)
            {
                if (_committed || _disposed)
                    throw new InvalidOperationException("Cannot add commit callback to a transaction that is already committed or disposed.");
                _commitCallbacks.Add(callback);
            }

            public void Commit()
            {
                if (_committed || _disposed) return;

                var context = new TransactionContext<TKey, TNodeData, TEdgeData>(_parent._storage);
                try
                {
                    foreach (var cmd in _commands)
                    {
                        var executableCmd = WrapWithErrorHandler(cmd);
                        executableCmd.Execute(context);
                    }

                    _committed = true;

                    foreach (var evt in context.PendingEvents)
                    {
                        try { _parent.PublishEvent(evt); }
                        catch { }
                    }

                    foreach (var callback in _commitCallbacks)
                    {
                        try { callback(); }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    try { RollbackCommands(context); }
                    catch (Exception rollbackEx)
                    {
                        throw new AggregateException("Transaction commit failed and rollback encountered errors.", ex, rollbackEx);
                    }
                    throw;
                }
                finally
                {
                    Interlocked.CompareExchange(ref _parent._activeTransaction, null, this);
                    _parent.ReleaseWriteLock();
                }
            }


            public void Rollback()
            {
                if (_committed || _disposed) return;
                try
                {
                    var context = new TransactionContext<TKey, TNodeData, TEdgeData>(_parent._storage);
                    RollbackCommands(context);
                }
                finally
                {
                    Interlocked.CompareExchange(ref _parent._activeTransaction, null, this);
                    _parent.ReleaseWriteLock();
                    _committed = true;
                    _disposed = true;
                }
            }

            private ICommand WrapWithErrorHandler(ICommand cmd)
            {
                return _errorHandler != null
                    ? new ErrorHandlingCommand(cmd, _errorHandler)
                    : cmd;
            }

            private void RollbackCommands(TransactionContext<TKey, TNodeData, TEdgeData> context)
            {
                for (int i = _commands.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        WrapWithErrorHandler(_commands[i]).Undo(context);
                    }
                    catch { }
                }
            }

            public void Dispose()
            {
                if (!_committed && !_disposed)
                {
                    try
                    {
                        var context = new TransactionContext<TKey, TNodeData, TEdgeData>(_parent._storage);
                        RollbackCommands(context);
                    }
                    catch { }
                    finally
                    {
                        Interlocked.CompareExchange(ref _parent._activeTransaction, null, this);
                        _parent.ReleaseWriteLock();
                        _committed = true;
                        _disposed = true;
                    }
                }
                else
                {
                    Interlocked.CompareExchange(ref _parent._activeTransaction, null, this);
                    _disposed = true;
                }
            }

        }

        private class ErrorHandlingCommand : ICommand
        {
            private readonly ICommand _inner;
            private readonly IErrorHandler _errorHandler;

            public ErrorHandlingCommand(ICommand inner, IErrorHandler errorHandler)
            {
                _inner = inner;
                _errorHandler = errorHandler;
            }

            public void Execute(TransactionContextBase context)
            {
                try { _inner.Execute(context); }
                catch (GraphException) { throw; }
                catch (Exception ex)
                {
                    var error = new GraphError(GraphErrorType.InternalError, ex.Message, ex);
                    if (!_errorHandler.HandleError(error))
                        throw new GraphException(error);
                }
            }

            public void Undo(TransactionContextBase context)
            {
                try { _inner.Undo(context); }
                catch (GraphException) { throw; }
                catch (Exception ex)
                {
                    var error = new GraphError(GraphErrorType.InternalError, $"Undo failed: {ex.Message}", ex);
                    if (!_errorHandler.HandleError(error))
                        throw new GraphException(error);
                }
            }
        }


        public void Dispose()
        {
            if (_disposed) return;

            var activeTx = Interlocked.Exchange(ref _activeTransaction, null);
            if (activeTx != null)
            {
                try { activeTx.Dispose(); }
                catch { }
            }

            lock (_eventSubject)
            {
                if (!_eventCompleted)
                {
                    _eventSubject.OnCompleted();
                    _eventCompleted = true;
                }
            }
            _lock.Dispose();
            _eventSubject.Dispose();
            _disposed = true;
        }
    }
}
