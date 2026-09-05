using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>三区数据契约存储端口。append-only 事件库语义（写即触发、观测=查数据）。</summary>
public interface IZoneStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<long> AppendInputAsync(InputRecord record, CancellationToken ct = default);

    Task<long> AppendOutputAsync(OutputRecord record, CancellationToken ct = default);

    Task<long> AppendEventAsync(EventRecord record, CancellationToken ct = default);

    Task<IReadOnlyList<InputRecord>> ListInputsAsync(string? threadId = null, long? afterId = null, int limit = 100, CancellationToken ct = default);

    Task<IReadOnlyList<OutputRecord>> ListOutputsAsync(string? threadId = null, long? afterId = null, int limit = 100, CancellationToken ct = default);

    Task<IReadOnlyList<EventRecord>> ListEventsAsync(string? threadId = null, long? afterId = null, int limit = 100, CancellationToken ct = default);
}
