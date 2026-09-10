using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalWorkflowFramework.StorageFramework.Abstractions;

/// <summary>checkpoint 存储端口（LangGraph 兼容：ThreadId/CheckpointNs/CheckpointId/父链/Channel 快照）。</summary>
public interface ICheckpointStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<Checkpoint?> GetAsync(string threadId, string? checkpointNs = null, string? checkpointId = null, CancellationToken ct = default);

    Task<string> PutAsync(Checkpoint checkpoint, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListAsync(string threadId, string? checkpointNs = null, CancellationToken ct = default);
}
