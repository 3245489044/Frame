using System.Collections.Generic;
using BasicMethod.Declarative.Graph;

namespace Share.Signal;

/// <summary>信号端口契约（<c>[Node] fn=</c> 的运行时投影；刻意不暴露语言层类型）。</summary>
public sealed record SignalPortSpec(
    string Name,
    string FuncName,
    IReadOnlyList<string> InputPorts,
    IReadOnlyList<string> OutputPorts);

/// <summary>观测事件契约（<c>[Obs] kind=event</c> 的运行时投影）。</summary>
public sealed record SignalObsEvent(
    string Name,
    string? Entity,
    IReadOnlyList<string> Required,
    IReadOnlyList<string> Metrics,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Enums);

/// <summary>
/// 信号声明面的一次性不可变快照（SCF 唯一语义来源）。
/// <para>消费方：<see cref="SignalStateMachine"/>（迁移）、<see cref="SignalAckEvaluator"/>（回执）、
/// <see cref="SignalProgressEvaluator"/>（推进）、观测落库。</para>
/// </summary>
public sealed record SignalSpec(
    string SourceFile,
    IReadOnlyDictionary<string, string> LifecycleTransitions,
    IReadOnlyList<SignalLifecycleEdge> LifecycleEdges,
    DecisionGraph? AckGraph,
    DecisionGraph? ProgressGraph,
    IReadOnlyDictionary<string, SignalPortSpec> Ports,
    IReadOnlyList<string> States,
    IReadOnlyList<string> Kinds,
    IReadOnlyList<string> Phases,
    IReadOnlyList<SignalObsEvent> Events,
    DecisionGraph? SourceGraph = null,
    IReadOnlyList<string>? FlowSteps = null)
{
    /// <summary>回执判定是否可求值。</summary>
    public bool HasAck => AckGraph is not null;

    /// <summary>推进判定是否可求值。</summary>
    public bool HasProgress => ProgressGraph is not null;

    /// <summary>来源映射是否可求值。</summary>
    public bool HasSource => SourceGraph is not null;

    /// <summary>六段编排的声明步骤序（空 = 未声明）。</summary>
    public IReadOnlyList<string> Steps => FlowSteps ?? System.Array.Empty<string>();
}
