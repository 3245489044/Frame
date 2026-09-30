namespace Share.CircuitFramework.Projection;

/// <summary>
/// 装配步骤：一个装配原子单位（步骤级）。对应一只电路节点（<see cref="Core.CircuitNode"/>）：
/// <list type="bullet">
///   <item><see cref="Id"/>：电路内节点 Id（唯一）；</item>
///   <item><see cref="StepKind"/>：步骤实现名（映射 <see cref="Core.ICircuitNode"/> 执行体）；</item>
///   <item><see cref="InPorts"/>：依赖产物名；</item>
///   <item><see cref="OutPorts"/>：产生产物名。</item>
/// </list>
/// </summary>
public sealed record CircuitStepSpec(
    string Id,
    string StepKind,
    IReadOnlyList<string> InPorts,
    IReadOnlyList<string> OutPorts);

/// <summary>产物→依赖 传递边（Wire：FromNode.FromPort → ToNode.ToPort）。</summary>
public sealed record CircuitFlowSpec(
    string FromNode, string FromPort,
    string ToNode, string ToPort);

/// <summary>
/// 出口绑定：执行结束后把 Exit 收集到的最终产物投递回调用方（典型 = 闭包写回装配上下文）。
/// 使「出口产物名 ↔ 回投目标」只在一处声明（消除回投代码里再抄一遍出口名）。
/// </summary>
public sealed record ExitBinding(string ExitPort, Action<object?> Apply);

/// <summary>
/// 声明式装配配方（装配拓扑的单一真源）：步骤 + 传递边 + 入口（外部绑定产物，如 Config）+ 出口（最终产物集）。
/// 经 <see cref="CircuitRecipeProjector"/> 投影为电路，供编译期校验（拓扑/产物唯一/环）、影响面分析与拓扑驱动执行。
/// </summary>
public sealed record CircuitRecipeSpec(
    string CircuitId,
    /// <summary>外部绑定产物（如 "config"）；经 EntrySymbol 供给同名输入端口。</summary>
    IReadOnlyList<string> EntryPorts,
    /// <summary>最终产物（形如 "NodeId.Port"）；经 ExitSymbol 收集后交 <see cref="ExitBinding"/> 回投。</summary>
    IReadOnlyList<string> ExitPorts,
    IReadOnlyList<CircuitStepSpec> Steps,
    IReadOnlyList<CircuitFlowSpec> Flows);
