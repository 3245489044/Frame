
using System.Collections.Concurrent;
using TConfig.ContextFrame.Abstractions;

namespace Modules.InternalStorage.Core;

public class StorageEngine : IStorageEngine
{
    private readonly ConcurrentDictionary<string, IStorageCell> _sessions = new();
    private readonly ISnapshotManager _snapshotManager;
    private StorageEngineState _state = StorageEngineState.Created;
    private bool _disposed;

    public string Name { get; }

    public IStorageCell RootCell { get; }

    public StorageEngineState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                var oldState = _state;
                _state = value;
                StateChanged?.Invoke(this,
                    new StorageEngineStateChangedEventArgs(Name, oldState, value));
            }
        }
    }

    public event EventHandler<StorageEngineStateChangedEventArgs>? StateChanged;

    public StorageEngine(string? name = null, ISnapshotManager? snapshotManager = null)
    {
        Name = name ?? $"StorageEngine-{Guid.NewGuid():N}";
        RootCell = new StorageCell($"{Name}.Root");
        _snapshotManager = snapshotManager ?? new SnapshotManager();
        State = StorageEngineState.Running;
    }



    public IStorageCell CreateSession(string sessionId)
    {
        ThrowIfDisposed();

        var sessionCell = new StorageCell($"{Name}.Session.{sessionId}", RootCell);
        if (!_sessions.TryAdd(sessionId, sessionCell))
        {
            throw new InvalidOperationException(
                $"会话 '{sessionId}' 已存在。请使用不同的会话标识，或先释放现有会话。");
        }
        return sessionCell;
    }

    public IStorageCell? GetSession(string sessionId)
    {
        ThrowIfDisposed();
        return _sessions.TryGetValue(sessionId, out var cell) ? cell : null;
    }

    public void DisposeSession(string sessionId)
    {
        ThrowIfDisposed();

        if (_sessions.TryRemove(sessionId, out var cell))
        {
            ClearSessionCell(cell);
        }
    }

    public IEnumerable<string> GetActiveSessionIds()
    {
        return _sessions.Keys;
    }


    public ILayeredSnapshot CreateSnapshot(string sessionId)
    {
        ThrowIfDisposed();

        var cell = GetSession(sessionId)
            ?? throw new InvalidOperationException($"会话 '{sessionId}' 不存在。");

        return _snapshotManager.CreateSnapshot(cell);
    }

    public void RestoreSnapshot(string sessionId, ILayeredSnapshot snapshot)
    {
        ThrowIfDisposed();

        var cell = GetSession(sessionId)
            ?? throw new InvalidOperationException($"会话 '{sessionId}' 不存在。");

        _snapshotManager.RestoreSnapshot(cell, snapshot);
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var sessionId in _sessions.Keys.ToList())
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                ClearSessionCell(session);
            }
        }

        RootCell.ClearLayer(StorageLayer.Config);

        State = StorageEngineState.Disposed;
    }

    private static void ClearSessionCell(IStorageCell cell)
    {
        cell.ClearLayer(StorageLayer.Config);
        cell.ClearLayer(StorageLayer.Session);
        cell.ClearLayer(StorageLayer.Transient);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(Name, "存储引擎已释放");
    }
}
