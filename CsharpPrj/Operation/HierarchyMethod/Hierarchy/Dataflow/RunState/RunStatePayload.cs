using System;
using System.Collections.Generic;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.RunState;

/// <summary>
/// 运行态的层结论载荷投影（**唯一投影点**）—— 把 <see cref="LevelRunState"/> 展平为
/// <see cref="Modules.DeclarativeDataflow.Processing.LevelProcessResult.Payload"/> 的点分约定键。
/// <para>
/// 约定键显式登记于此（框架本体不解释 Payload 键语义，故"约定"必须有单一来源，禁各处手写字符串）：
/// <c>runState.stage</c> / <c>runState.actorKind</c> / <c>runState.actorId</c> /
/// <c>runState.round</c> / <c>runState.elapsedMs</c> / <c>runState.origin</c> /
/// <c>runState.statusText</c> / <c>runState.note</c> / <c>runState.metrics</c>。
/// </para>
/// </summary>
public static class RunStatePayload
{
    /// <summary>约定键前缀。</summary>
    public const string Prefix = "runState";

    /// <summary>受控阶段键（<see cref="RunStateStage"/>）。</summary>
    public const string StageKey = "runState.stage";

    /// <summary>执行体类别键（<see cref="ActorKinds"/>）。</summary>
    public const string ActorKindKey = "runState.actorKind";

    /// <summary>执行体标识键。</summary>
    public const string ActorIdKey = "runState.actorId";

    /// <summary>轮次键。</summary>
    public const string RoundKey = "runState.round";

    /// <summary>已运行毫秒键。</summary>
    public const string ElapsedMsKey = "runState.elapsedMs";

    /// <summary>事实来源键（<see cref="RunStateOrigin"/> 名称）。</summary>
    public const string OriginKey = "runState.origin";

    /// <summary>原始状态文本键（保真追溯）。</summary>
    public const string StatusTextKey = "runState.statusText";

    /// <summary>决策 / 诊断文本键。</summary>
    public const string NoteKey = "runState.note";

    /// <summary>度量键（<c>IReadOnlyDictionary&lt;string,double&gt;</c>）。</summary>
    public const string MetricsKey = "runState.metrics";

    /// <summary>是否携带运行态（供消费方判空）。</summary>
    public const string AvailableKey = "runState.available";

    /// <summary>把运行态写入载荷字典（null 运行态 → 写 available=false，不写其它键）。</summary>
    /// <param name="payload">目标载荷（就地写入；调用方持有其所有权）。</param>
    /// <param name="state">运行态（可空）。</param>
    public static void Write(IDictionary<string, object?> payload, LevelRunState? state)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (state is null)
        {
            payload[AvailableKey] = false;
            return;
        }

        payload[AvailableKey] = true;
        payload[StageKey] = state.Stage;
        payload[ActorKindKey] = state.Actor?.Kind;
        payload[ActorIdKey] = state.Actor?.Id;
        payload[RoundKey] = state.Round;
        payload[ElapsedMsKey] = state.ElapsedMs;
        payload[OriginKey] = state.Origin.ToString();
        payload[StatusTextKey] = state.StatusText;
        payload[NoteKey] = state.Note;
        if (state.Metrics is { Count: > 0 }) payload[MetricsKey] = state.Metrics;
    }

    /// <summary>从载荷读回运行态（缺 stage/available=false → null；供消费方 / 断言使用）。</summary>
    public static LevelRunState? Read(IReadOnlyDictionary<string, object?>? payload, Modules.DeclarativeDataflow.Model.LevelRef reference)
    {
        if (payload is null || !payload.TryGetValue(AvailableKey, out var available) || available is not true) return null;
        var stage = payload.TryGetValue(StageKey, out var s) ? s as string : null;
        var kind = payload.TryGetValue(ActorKindKey, out var k) ? k as string : null;
        var id = payload.TryGetValue(ActorIdKey, out var i) ? i as string : null;
        var origin = payload.TryGetValue(OriginKey, out var o) && o is string originText
            && Enum.TryParse<RunStateOrigin>(originText, out var parsed) ? parsed : RunStateOrigin.Derived;

        return new LevelRunState(
            reference,
            RunStateStage.Normalize(stage),
            ActorRef.OrNull(kind, id),
            payload.TryGetValue(RoundKey, out var r) ? r as int? : null,
            payload.TryGetValue(StatusTextKey, out var st) ? st as string : null,
            ElapsedMs: payload.TryGetValue(ElapsedMsKey, out var e) ? e as long? : null,
            Metrics: payload.TryGetValue(MetricsKey, out var m) ? m as IReadOnlyDictionary<string, double> : null,
            Note: payload.TryGetValue(NoteKey, out var n) ? n as string : null,
            Origin: origin);
    }
}
