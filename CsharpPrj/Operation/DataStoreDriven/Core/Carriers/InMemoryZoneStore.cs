using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 内存三区数据契约存储（append-only 事件库语义）：ur_inputs/ur_outputs/ur_events 的内存承载。
/// 用于非 Sqlite 后端的 <see cref="FacadeDataStore"/> 三区占位，以及测试/原型；进程重启即失。
/// 持久化三区见 <see cref="NodeZoneStore"/>（三库收敛后经节点库承载）。
/// 实现 <see cref="IZoneStoreReactive"/>：提交即发布 <see cref="Writes"/>（写入即触发，供反应式触发订阅）。
/// </summary>
public sealed class InMemoryZoneStore : IZoneStoreReactive
{
    private readonly List<InputRecord> _inputs = new();
    private readonly List<OutputRecord> _outputs = new();
    private readonly List<EventRecord> _events = new();
    private readonly object _lock = new();
    private readonly Subject<ZoneWriteSignal> _subject = new();
    // 各区独立自增 id 计数器（对齐 Sqlite 每表 AUTOINCREMENT；取代基于 Count 的派生，避免未来删除/重建时 id 冲突）。
    private long _inputSeq;
    private long _outputSeq;
    private long _eventSeq;

    public InMemoryZoneStore()
    {
        Writes = _subject.AsObservable();
    }

    public IObservable<ZoneWriteSignal> Writes { get; }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<long> AppendInputAsync(InputRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var id = AppendTo(_inputs, ref _inputSeq, newId => new InputRecord
        {
            Id = newId,
            ThreadId = record.ThreadId,
            InputKey = record.InputKey,
            PayloadJson = record.PayloadJson ?? "{}",
            CreatedAt = record.CreatedAt ?? PersistenceHelper.NowIso(),
        });
        Publish(new ZoneWriteSignal { Zone = ZoneKind.Input, Id = id, ThreadId = record.ThreadId, Key = record.InputKey });
        return Task.FromResult(id);
    }

    public Task<long> AppendOutputAsync(OutputRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var id = AppendTo(_outputs, ref _outputSeq, newId => new OutputRecord
        {
            Id = newId,
            ThreadId = record.ThreadId,
            InputKey = record.InputKey,
            PayloadJson = record.PayloadJson ?? "{}",
            Status = record.Status,
            CreatedAt = record.CreatedAt ?? PersistenceHelper.NowIso(),
        });
        Publish(new ZoneWriteSignal { Zone = ZoneKind.Output, Id = id, ThreadId = record.ThreadId, Key = record.InputKey });
        return Task.FromResult(id);
    }

    public Task<long> AppendEventAsync(EventRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var id = AppendTo(_events, ref _eventSeq, newId => new EventRecord
        {
            Id = newId,
            ThreadId = record.ThreadId,
            EventType = record.EventType,
            EventKey = record.EventKey,
            PayloadJson = record.PayloadJson ?? "{}",
            CreatedAt = record.CreatedAt ?? PersistenceHelper.NowIso(),
        });
        Publish(new ZoneWriteSignal { Zone = ZoneKind.Event, Id = id, ThreadId = record.ThreadId, Key = record.EventKey });
        return Task.FromResult(id);
    }

    private void Publish(ZoneWriteSignal signal) => ZoneSignalPublisher.Publish(_subject, "InMemoryZoneStore", signal);

    public Task<IReadOnlyList<InputRecord>> ListInputsAsync(string? threadId = null, long? afterId = null, int limit = 100, CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult(Paged(_inputs, i => i.ThreadId, i => i.Id, threadId, afterId, limit));
    }

    public Task<IReadOnlyList<OutputRecord>> ListOutputsAsync(string? threadId = null, long? afterId = null, int limit = 100, CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult(Paged(_outputs, o => o.ThreadId, o => o.Id, threadId, afterId, limit));
    }

    public Task<IReadOnlyList<EventRecord>> ListEventsAsync(string? threadId = null, long? afterId = null, int limit = 100, CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult(Paged(_events, e => e.ThreadId, e => e.Id, threadId, afterId, limit));
    }

    /// <summary>锁内追加记录：生成自增 Id → 落列表 → 返回 Id（发布移到锁外，避免持锁同步回调订阅者）。</summary>
    private long AppendTo<T>(List<T> list, ref long seq, Func<long, T> create)
    {
        lock (_lock)
        {
            var id = IdGenerator.Next(ref seq);
            list.Add(create(id));
            return id;
        }
    }

    /// <summary>三区 List 统一分页骨架：threadId/afterId 过滤 + 按 Id 升序 + limit 截断（三方法收敛为一处）。</summary>
    private static IReadOnlyList<T> Paged<T>(IReadOnlyList<T> source,
        Func<T, string?> threadIdOf, Func<T, long> idOf,
        string? threadId, long? afterId, int limit)
    {
        var list = source
            .Where(x => threadId is null || threadIdOf(x) == threadId)
            .Where(x => afterId is null || idOf(x) > afterId)
            .OrderBy(idOf)
            .Take(Math.Max(0, limit))
            .ToList();
        return list;
    }
}
