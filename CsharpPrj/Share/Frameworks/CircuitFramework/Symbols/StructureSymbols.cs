using Share.CircuitFramework.Core;

namespace Share.CircuitFramework.Symbols;

/// <summary>Call 指向的目标电路引用（跨版本唯一，用于调用链接枚举与影响面分析）。</summary>
public sealed record CalleeRef(string CircuitId, string? Version = null)
{
    /// <summary>规整后的跨版本唯一标识，如 "doc.workflow.order#v3"。</summary>
    public string CanonicalId =>
        string.IsNullOrWhiteSpace(Version) ? CircuitId : $"{CircuitId}#{Version}";

    public override string ToString() => CanonicalId;
}

/// <summary>Call：函数调用链中的一环，承载节点引用另一个 Circuit 作为子计算。</summary>
public sealed class CallSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Call;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.Structure;

    /// <summary>承载该 Call 的节点（调用者）。</summary>
    public required string CallerNodeId { get; init; }

    /// <summary>被调用的目标电路引用。</summary>
    public required CalleeRef Target { get; init; }
}

/// <summary>Composite：一组节点 + 连线的内联子电路（不落单独文件）。</summary>
public sealed class CompositeSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Composite;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.Structure;

    /// <summary>承载该 Composite 的节点。</summary>
    public required string NodeId { get; init; }

    /// <summary>内联子电路。</summary>
    public required Circuit Inner { get; init; }
}

/// <summary>SubCircuit：独立命名的可复用电路，可被 Call 引用。</summary>
public sealed class SubCircuitSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.SubCircuit;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.Structure;

    /// <summary>承载该 SubCircuit 的节点。</summary>
    public required string NodeId { get; init; }

    /// <summary>被复用的命名电路的引用。</summary>
    public required CalleeRef Target { get; init; }
}

/// <summary>Entry：电路总入口投影，声明顶层入参。</summary>
public sealed class EntrySymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Entry;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.Structure;

    public required string Port { get; init; }
    public string? DataType { get; init; }
}

/// <summary>Exit：电路总出口投影，声明顶层出参。</summary>
public sealed class ExitSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Exit;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.Structure;

    public required string Port { get; init; }
    public string? DataType { get; init; }
}