namespace Share.CircuitFramework.Projection;

/// <summary>
/// 分支声明（R1 声明面）：判定键为真 → <c>ThenNodeId</c> 激活、<c>ElseNodeId</c> 抑制；为假则反之。
/// 判定载体 = 结构化数据键（归一规则同 <c>CircuitFlowSpec.When</c>），非表达式文本。
/// 投影为 <c>Symbols.BranchSymbol</c>；执行面 = <c>Compiler.CircuitRuntimeIndex</c> 路由表 + 引擎后继门控。
/// </summary>
/// <param name="NodeId">判定承载节点（须已声明步骤；其出端口即判定来源）。</param>
/// <param name="JudgeKey">判定键（"NodeId.Port" / 入口名 / "$..."）。</param>
/// <param name="ThenNodeId">判定为真时激活的目标节点。</param>
/// <param name="ElseNodeId">判定为假时激活的目标节点（可空 = 单边分支，即只门控 then）。</param>
public sealed record CircuitBranchSpec(string NodeId, string JudgeKey, string ThenNodeId, string? ElseNodeId = null);

/// <summary>
/// 多路分派声明（R1 声明面）：选择键取值命中 <c>Routes</c> 目标则激活，未命中则 <c>DefaultNodeId</c> 激活（互斥）。
/// 投影为 <c>Symbols.SwitchSymbol</c>；执行面 = 引擎后继门控（AnyOf / NoneOf 路由）。
/// </summary>
/// <param name="NodeId">分派承载节点（须已声明步骤）。</param>
/// <param name="SelectorKey">选择键（结构化数据键）。</param>
/// <param name="Routes">取值 → 目标节点。</param>
/// <param name="DefaultNodeId">未命中任何 route 时激活的默认目标（可空 = 命中外不激活）。</param>
public sealed record CircuitSwitchSpec(
    string NodeId,
    string SelectorKey,
    IReadOnlyDictionary<string, string> Routes,
    string? DefaultNodeId = null);
