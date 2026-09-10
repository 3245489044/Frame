using System.Threading;
using System.Threading.Tasks;

namespace UniversalWorkflowFramework.StorageFramework.Abstractions;

/// <summary>状态快照存储端口（ur_snapshots）。按 thread（+ session）读写最新快照，支撑崩溃恢复 / 跨会话恢复。</summary>
public interface ISnapshotStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task SaveAsync(string threadId, string? sessionId, string snapshotJson, CancellationToken ct = default);

    Task<string?> LoadAsync(string threadId, string? sessionId, CancellationToken ct = default);
}
