using Share.CircuitFramework.Core;

namespace Share.CircuitFramework.Compiler;

/// <summary>
/// 执行能力表（单一来源）：已被执行面真正消费的符号集（CFG-101 的唯一判据源）。
/// <para>
/// 两档消费路径（都算「已消费」，故 CFG-101 均不报；差别只在谁读符号）：
/// <list type="bullet">
///   <item><b>引擎/索引逐读</b>：<see cref="CircuitRuntimeIndex.Build"/> 为其产出绑定/路由/策略
///     —— Entry/Exit/Literal/Param/Projection/Fold/Unfold/Branch/Switch/Retry/Fallback/CatchRoute/TryGuard。</item>
///   <item><b>宿主节点消费</b>：<see cref="Execution.CircuitEngine"/> 不直接读这些镜像符号，
///     由调用方注入的黑盒节点消费 —— SubCircuit/Loop/WhileCond/Recursion/BaseCase（<see cref="Execution.RegionNodeBase"/>）、
///     Call（<see cref="Execution.SubCircuitNode"/>）；镜像本身仅供 <see cref="Analysis.CircuitCallGraph"/> / 离线审阅。</item>
/// </list>
/// 表外符号（如 Sequence/Fork）由 <see cref="CircuitValidator"/> 的声明-执行对账（CFG-101）可见化（Warning），「声明面能写、执行面没做」不可能静默。
/// </para>
/// <para>
/// 变更纪律：把一类符号加进本表 = **同一提交内**必须同时给出执行面与「声明 ↔ 执行」对拍用例，
/// 否则就出现「表内有、引擎无」的假支持（本表即 CFG-101 的唯一判据来源）。
/// </para>
/// </summary>
public static class CircuitExecutionCapabilities
{
    private static readonly HashSet<CircuitSymbolKind> ConsumedKinds = new()
    {
        CircuitSymbolKind.Entry,      // 入口投影：$entry.{Port} → 同名入端口
        CircuitSymbolKind.Exit,       // 出口收集：$NodeId.Port → 结果字典
        CircuitSymbolKind.Literal,    // 常量注入（不查上下文）
        CircuitSymbolKind.Param,      // 端口具名绑定（上游端口引用 / 外部值名）
        CircuitSymbolKind.Projection, // 字段路径取值
        CircuitSymbolKind.Fold,       // 多路聚合为数组
        CircuitSymbolKind.Unfold,     // 一路扇出多端口
        CircuitSymbolKind.SubCircuit, // 区域化执行：声明为区域时由 Execution.RegionNodeBase 驱动子计划
        CircuitSymbolKind.Loop,       // 区域有界重复（FixedRepeats）：RegionNodeBase 按声明计次驱动子计划
                                      // （声明由 CircuitRecipeProjector 投影为 Loop 镜像；对拍见 CircuitRegionExecutionTests）
        CircuitSymbolKind.WhileCond,  // G2 条件迭代（WhileTrue/UntilTrue）：RegionNodeBase 按判定端口驱动子计划
                                      // （声明由 CircuitRecipeProjector 投影为 WhileCond 镜像；对拍见 CircuitRegionExecutionTests）
        CircuitSymbolKind.Retry,      // G4 容错：引擎按 Retry 声明重试/退避（机制侧；阈值/熔断归宿主）
        CircuitSymbolKind.Fallback,   // G4 容错：主路径失败 → 备选节点（机制侧）
        CircuitSymbolKind.CatchRoute, // G4 容错：失败后路由到补救节点（机制侧）
        CircuitSymbolKind.TryGuard,   // G4 容错：受保护节点失败 → 吞并继续（机制侧）
        CircuitSymbolKind.Recursion,  // F3 递归执行化：自引用有界区域（RegionNodeBase 按 Recursion 计划驱动子计划；
                                      // 声明由 CircuitRecipeProjector 投影为 RecursionSymbol 镜像；对拍见 CircuitRegionExecutionTests）
        CircuitSymbolKind.BaseCase,   // F3：递归基例声明（RecursionSymbol.BaseCase；执行面 = 区域基例端口判定）
        CircuitSymbolKind.Branch,     // R1 分支执行化：后继激活/抑制（声明 → BranchSymbol；执行面 = CircuitRuntimeIndex 路由表 + 引擎门控；
                                      // 对拍见 CircuitFramework.Tests\CircuitBranchExecutionTests）
        CircuitSymbolKind.Switch,     // R1 多路分派：选择键取值路由（同上；对拍见 CircuitBranchExecutionTests）
        CircuitSymbolKind.Call,       // R2 调用执行化：子电路调用（声明 → CallSymbol；执行面 = Execution.SubCircuitNode；
                                      // 对拍见 CircuitFramework.Tests\CircuitCallExecutionTests）
    };

    /// <summary>已被执行后端消费的符号种类（只读；顺序不承载语义）。</summary>
    public static IReadOnlyCollection<CircuitSymbolKind> Consumed => ConsumedKinds;

    /// <summary>该符号种类是否已被执行后端消费（CFG-101 对账判据）。</summary>
    public static bool IsConsumed(CircuitSymbolKind kind) => ConsumedKinds.Contains(kind);
}
