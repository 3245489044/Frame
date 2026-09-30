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
    string ToNode, string ToPort,
    /// <summary>
    /// 受控边判定（G3，可选）：判定键（端口值 / 入口值），非空时本边仅在判定为真时激活（目标节点本次不执行）。
    /// 载体 = 结构化数据键，不是表达式文本（对齐 SEP.JudgmentCarrierStructural）；缺省 null = 无条件边（源兼容）。
    /// </summary>
    string? When = null);

/// <summary>
/// 出口绑定：执行结束后把 Exit 收集到的最终产物投递回调用方（典型 = 闭包写回装配上下文）。
/// 使「出口产物名 ↔ 回投目标」只在一处声明（消除回投代码里再抄一遍出口名）。
/// </summary>
public sealed record ExitBinding(string ExitPort, Action<object?> Apply)
{
    /// <summary>由「节点 Id + 输出端口名」组合出口名（<c>"NodeId.Port"</c> 只在此处拼一次，不再逐处手抄字面量）。</summary>
    public static ExitBinding Of(string nodeId, string port, Action<object?> apply)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(port);
        ArgumentNullException.ThrowIfNull(apply);

        return new ExitBinding($"{nodeId}.{port}", apply);
    }

    /// <summary>由「节点 Id + 输出端口名」组合出口名，并收敛类型（消除回投侧逐处 <c>v as T</c>）。</summary>
    public static ExitBinding Of<T>(string nodeId, string port, Action<T?> apply) where T : class
    {
        ArgumentNullException.ThrowIfNull(apply);

        return Of(nodeId, port, value => apply(value as T));
    }
}

/// <summary>
/// 常量注入声明（B3-1）：把 <see cref="Value"/> 直接绑到步骤 <see cref="NodeId"/> 的入端口 <see cref="Port"/>
/// （不查上下文；供「逐步骤差异化常量」供给，如逐 op 参数注入）。
/// </summary>
public sealed record CircuitLiteralSpec(string NodeId, string Port, object? Value);

/// <summary>
/// 控制边声明（无端口对的顺序依赖）：前驱只约束后继的执行先后，**不经数据键**（故无法表达为 Wire）。
/// 投影为 <see cref="Symbols.SequenceSymbol"/> 镜像，使「谁调谁」在电路层可见（编译校验 / 静态分析 / CFG-101 对账）；
/// 执行序仍由 Wire + 声明序承担（镜像不参与执行 —— 与区域 Loop 镜像同族口径）。
/// </summary>
public sealed record CircuitControlEdgeSpec(string FromNode, string ToNode);

/// <summary>
/// 声明式装配配方（装配拓扑的单一真源）：步骤 + 传递边 + 入口（外部绑定产物，如 Config）+ 出口（最终产物集）
/// + 常量注入 + 区域（环/子计划封在普通节点内）+ 控制边（无端口对的顺序依赖）。
/// 经 <see cref="CircuitRecipeProjector"/> 投影为电路，供编译期校验（拓扑/产物唯一/环）、影响面分析与拓扑驱动执行。
/// </summary>
public sealed record CircuitRecipeSpec(
    string CircuitId,
    /// <summary>外部绑定产物（如 "config"）；经 EntrySymbol 供给同名输入端口。</summary>
    IReadOnlyList<string> EntryPorts,
    /// <summary>最终产物（形如 "NodeId.Port"）；经 ExitSymbol 收集后交 <see cref="ExitBinding"/> 回投。</summary>
    IReadOnlyList<string> ExitPorts,
    IReadOnlyList<CircuitStepSpec> Steps,
    IReadOnlyList<CircuitFlowSpec> Flows,
    /// <summary>常量注入（投影为 LiteralSymbol；缺省/空 = 无常量供给）。</summary>
    IReadOnlyList<CircuitLiteralSpec>? Literals = null,
    /// <summary>
    /// 区域声明（缺省/空 = 无区域）：承载节点须已在 <see cref="Steps"/> 中声明。
    /// 判据（CFG-019~023）由 <see cref="Compiler.RegionValidator"/> 在编译期给出；父图拓扑不含环。
    /// </summary>
    IReadOnlyList<CircuitRegionSpec>? Regions = null,
    /// <summary>
    /// 控制边声明（缺省/空 = 无控制边）：两端须已在 <see cref="Steps"/> 中声明；
    /// 投影为 Sequence 镜像（缺省不参与执行 —— 能力表不含 Sequence ⇒ CFG-101 可见化该缺口，不静默）。
    /// </summary>
    IReadOnlyList<CircuitControlEdgeSpec>? ControlEdges = null,
    /// <summary>
    /// 重试声明（G4，缺省/空 = 无重试）：投影为 <see cref="Symbols.RetrySymbol"/>；承载节点存在性由 CFG-024 判红。
    /// </summary>
    IReadOnlyList<CircuitRetrySpec>? Retries = null,
    /// <summary>
    /// 失败路由声明（G4，缺省/空 = 无改道）：投影为 <see cref="Symbols.FallbackSymbol"/>；两端存在性由 CFG-024 判红。
    /// </summary>
    IReadOnlyList<CircuitFallbackSpec>? Fallbacks = null,
    /// <summary>
    /// 受保护节点声明（G4，缺省/空 = 无保护）：投影为 <see cref="Symbols.TryGuardSymbol"/>；节点存在性由 CFG-024 判红。
    /// </summary>
    IReadOnlyList<CircuitGuardSpec>? Guards = null);
