using Share.CircuitFramework.Core;

namespace Share.CircuitFramework.Compiler;

/// <summary>
/// 执行能力表（单一来源）：已被执行后端（<see cref="Execution.CircuitEngine"/>）真正消费的符号集。
/// <para>
/// <see cref="CircuitValidator"/> 的声明-执行对账（CFG-101）只报**表外**符号 ⇒ 「声明面能写、引擎没做」不可能静默：
/// 表内符号必有执行面（<see cref="CircuitRuntimeIndex.Build"/> 为其产出绑定或投影），表外符号必然可见化。
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
    };

    /// <summary>已被执行后端消费的符号种类（只读；顺序不承载语义）。</summary>
    public static IReadOnlyCollection<CircuitSymbolKind> Consumed => ConsumedKinds;

    /// <summary>该符号种类是否已被执行后端消费（CFG-101 对账判据）。</summary>
    public static bool IsConsumed(CircuitSymbolKind kind) => ConsumedKinds.Contains(kind);
}
