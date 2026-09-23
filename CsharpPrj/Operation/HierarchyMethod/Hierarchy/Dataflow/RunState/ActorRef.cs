using System;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.RunState;

/// <summary>
/// 执行体标识（运行态的一等维度，**与层级/阶段正交**）—— 答"谁在执行"。
/// <para>
/// 通用性来源：<see cref="Kind"/> 是开放词（见 <see cref="ActorKinds"/>），<see cref="Id"/> 为层内具体标识；
/// 「子智能体」只是 <c>Kind=agent</c> 的一个取值（如 cline / answer），框架不为其设专用类型或分支。
/// </para>
/// <para>
/// 无执行体（如容器层、纯簿记节点）用 <c>null</c> 表达，**不引入 None 占位实体**——
/// 故"某层与执行体无关"是数据缺省，不是约定。
/// </para>
/// </summary>
/// <param name="Kind">执行体类别（<see cref="ActorKinds"/> 常量；空白 = 非法）。</param>
/// <param name="Id">执行体标识（层内具体标识，如 cline / answer / 工具名；空白 = 非法）。</param>
public readonly record struct ActorRef(string Kind, string Id)
{
    /// <summary>是否为空引用（类别或标识缺失）。</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Kind) || string.IsNullOrWhiteSpace(Id);

    /// <summary>显示标签：agent 类直接给标识，其余给 <c>kind:id</c>（避免同名跨类混淆）。</summary>
    public string Label => IsEmpty
        ? string.Empty
        : string.Equals(Kind, ActorKinds.Agent, StringComparison.OrdinalIgnoreCase)
            ? Id
            : $"{Kind}:{Id}";

    /// <summary>构造代理类执行体（子智能体 / 无状态回答器）。</summary>
    public static ActorRef Agent(string id) => new(ActorKinds.Agent, id);

    /// <summary>构造工具类执行体。</summary>
    public static ActorRef Tool(string id) => new(ActorKinds.Tool, id);

    /// <summary>构造引擎类执行体（确定性核心 / 循环驱动）。</summary>
    public static ActorRef Engine(string id) => new(ActorKinds.Engine, id);

    /// <summary>空引用 / 非法 → null（供来源侧安全投影）。</summary>
    public static ActorRef? OrNull(string? kind, string? id)
        => string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(id) ? null : new ActorRef(kind!, id!);

    public override string ToString() => Label;
}

/// <summary>
/// 执行体类别受控词（**唯一来源**，SingleImplementation）—— 开放词表：扩展 = 增一条常量，消费方零改动。
/// </summary>
public static class ActorKinds
{
    /// <summary>代理类（子智能体 / 无状态回答器等 LLM 执行体）。</summary>
    public const string Agent = "agent";

    /// <summary>工具类（确定性工具执行）。</summary>
    public const string Tool = "tool";

    /// <summary>引擎类（循环驱动 / 确定性核心）。</summary>
    public const string Engine = "engine";
}
