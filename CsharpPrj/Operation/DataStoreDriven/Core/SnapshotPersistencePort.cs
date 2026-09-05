using System;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 快照承载的持久化端口实现：把 session 内存快照经 <see cref="ISnapshotStore"/> 落库 / 重放。
/// 后端无关（Sqlite/LiteDB/Graph/Local 均可复用），承载快照持久化（<see cref="ISnapshotStore"/>）。
/// </summary>
public sealed class SnapshotPersistencePort : IPersistencePort
{
    private readonly ISnapshotStore _snapshots;
    private readonly string _threadId;

    public SnapshotPersistencePort(ISnapshotStore snapshots, string? threadId = null)
    {
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        _threadId = string.IsNullOrWhiteSpace(threadId) ? "default" : threadId;
    }

    public Task PersistAsync(string sessionId, string snapshotJson, CancellationToken ct = default)
        => _snapshots.SaveAsync(_threadId, sessionId, snapshotJson, ct);

    public Task<string?> LoadAsync(string sessionId, CancellationToken ct = default)
        => _snapshots.LoadAsync(_threadId, sessionId, ct);
}
