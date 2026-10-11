using Share.CircuitFramework.Core;

namespace Share.CircuitFramework.Symbols;

/// <summary>效果方向：读 / 写。</summary>
public enum EffectKind
{
    /// <summary>读共享通道（值面经 ctx 的读侧）。</summary>
    Reads,

    /// <summary>写共享通道（值面经 ctx 的写侧）。</summary>
    Writes,
}

/// <summary>
/// 效果符号（G7）：把「经共享通道（值面 ctx）的副作用读写」显式化为图上一等关系 ——
/// 使「写上下文 → 下游读上下文」在图（ControlFlowGraph / CircuitReferenceGraph）上可见，
/// 并作为**顺序依赖**参与拓扑（Read-after-Write）。
/// <para>
/// 双通道策略（<c>CIRCUIT_CONTRACT §2</c>）：结构/产物面走端口（Wire），值面走 ctx；
/// 本符号即值面通道的可见化，不搬运运行值，只声明「谁读/写哪个通道的哪个主题」。
/// </para>
/// <para>框架零领域类型：通道名（<see cref="Channel"/>）与主题（<see cref="Subject"/>）由调用方给出。</para>
/// </summary>
public sealed class EffectSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind
        => Effect == EffectKind.Reads ? CircuitSymbolKind.ReadsState : CircuitSymbolKind.WritesState;

    public override CircuitSymbolCategory Category => CircuitSymbolCategory.Effect;

    /// <summary>承载该效果读写的节点。</summary>
    public required string NodeId { get; init; }

    /// <summary>效果方向（读 / 写）。</summary>
    public required EffectKind Effect { get; init; }

    /// <summary>共享通道名（如 <c>ctx.state</c>）。</summary>
    public required string Channel { get; init; }

    /// <summary>主题（通道内的读写键，如产物名 / 状态字段）。</summary>
    public required string Subject { get; init; }

    public override string ToString() => $"{NodeId} --{Effect}({Channel}:{Subject})";
}
