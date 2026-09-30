using Share.CircuitFramework.Core;

namespace Share.CircuitFramework.Symbols;

/// <summary>Param：节点输入端口上的具名绑定，把外部值/上游端口绑定到端口。</summary>
public sealed class ParamSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Param;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.Dataflow;

    /// <summary>目标节点。</summary>
    public required string TargetNodeId { get; init; }

    /// <summary>目标输入端口。</summary>
    public required string Port { get; init; }

    /// <summary>绑定来源：外部值名或上游端口引用（"node.port"）。</summary>
    public required string Source { get; init; }
}

/// <summary>Literal：常量注入点（连接常量到端口）。</summary>
public sealed class LiteralSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Literal;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.Dataflow;

    /// <summary>目标节点。</summary>
    public required string TargetNodeId { get; init; }

    /// <summary>目标输入端口。</summary>
    public required string Port { get; init; }

    /// <summary>常量值。</summary>
    public object? Value { get; init; }

    /// <summary>常量的数据类型标识。</summary>
    public string? ValueType { get; init; }
}

/// <summary>Fold：多路输入聚合为一个参数（数组化，供被调方一次收齐）。</summary>
public sealed class FoldSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Fold;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.Dataflow;

    /// <summary>目标节点。</summary>
    public required string TargetNodeId { get; init; }

    /// <summary>聚合后的目标输入端口。</summary>
    public required string Port { get; init; }

    /// <summary>参与聚合的多路来源端口引用。</summary>
    public required IReadOnlyList<PortRef> Sources { get; init; }
}

/// <summary>Unfold：单参数展开为多路扇出（按端口分别供给）。</summary>
public sealed class UnfoldSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Unfold;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.Dataflow;

    /// <summary>目标节点。</summary>
    public required string TargetNodeId { get; init; }

    /// <summary>来源（单一上游）。</summary>
    public required PortRef Source { get; init; }

    /// <summary>展开后的目标输入端口集合。</summary>
    public required IReadOnlyList<string> Ports { get; init; }
}

/// <summary>Projection：从复合对象取子字段供给端口（端口映射）。</summary>
public sealed class ProjectionSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Projection;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.Dataflow;

    /// <summary>目标节点。</summary>
    public required string TargetNodeId { get; init; }

    /// <summary>目标输入端口。</summary>
    public required string Port { get; init; }

    /// <summary>来源复合对象端口。</summary>
    public required PortRef Source { get; init; }

    /// <summary>目标端口数据来自源对象的字段路径（如 "user.address.city"）。</summary>
    public required string FieldPath { get; init; }
}