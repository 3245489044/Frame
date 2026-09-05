using System;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>数据区类型（ur_inputs / ur_outputs / ur_events）。</summary>
public enum ZoneKind
{
    Input,
    Output,
    Event
}

/// <summary>
/// 写入即触发信号：某条记录在数据区提交成功后发布（提交即发布、无中间态，对齐 §6.1）。
/// 供反应式触发（ReactiveTrigger）订阅，替代"轮询 DB 表"作为主机制（§6.2）。
/// </summary>
public sealed record ZoneWriteSignal
{
    public ZoneKind Zone { get; init; }

    /// <summary>被写入记录的 Id（append-only 主键）。</summary>
    public long Id { get; init; }

    public string? ThreadId { get; init; }

    /// <summary>业务键（InputKey / EventKey；Output 区可为空）。</summary>
    public string? Key { get; init; }
}

/// <summary>
/// 反应式数据区端口（可选，叠加门面）：在 <see cref="IZoneStore"/> 之上提供"写入即触发"信号源。
/// 实现方（InMemory/Sqlite 三区）在 Append*Async 提交成功后发布 <see cref="Writes"/>。
/// </summary>
public interface IZoneStoreReactive : IZoneStore
{
    /// <summary>提交即发布的可观测信号流（写入即触发）。</summary>
    IObservable<ZoneWriteSignal> Writes { get; }
}
