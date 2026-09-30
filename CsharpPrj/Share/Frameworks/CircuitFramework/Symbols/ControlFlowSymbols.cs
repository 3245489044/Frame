using Share.CircuitFramework.Core;

namespace Share.CircuitFramework.Symbols;

/// <summary>Sequence：严格有序的节点组（显式先后，隐含于边依赖之外）。</summary>
public sealed class SequenceSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Sequence;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.ControlFlow;

    /// <summary>按执行顺序排列的节点 id。</summary>
    public required IReadOnlyList<string> NodeIds { get; init; }
}

/// <summary>Branch：条件判定 → 选一路执行（if/else 的外部化）。</summary>
public sealed class BranchSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Branch;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.ControlFlow;

    /// <summary>承载判定条件的节点。</summary>
    public required string ConditionNodeId { get; init; }

    /// <summary>条件为真时的目标节点（then 分支）。</summary>
    public required string ThenNodeId { get; init; }

    /// <summary>条件为假时的目标节点（else 分支，可为空）。</summary>
    public string? ElseNodeId { get; init; }
}

/// <summary>Switch：多路分派 → 按值选一路（switch 的外部化）。</summary>
public sealed class SwitchSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Switch;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.ControlFlow;

    /// <summary>承载派发表达的节点。</summary>
    public required string SelectorNodeId { get; init; }

    /// <summary>取值 → 对应目标节点。</summary>
    public required IReadOnlyDictionary<string, string> Routes { get; init; }

    /// <summary>默认目标节点（可选）。</summary>
    public string? DefaultNodeId { get; init; }
}

/// <summary>Merge：多路汇合 → 带数据合并或任取其一的 join 点。</summary>
public sealed class MergeSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Merge;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.ControlFlow;

    /// <summary>汇合输出节点。</summary>
    public required string NodeId { get; init; }

    /// <summary>参与汇合的多个来源端口。</summary>
    public required IReadOnlyList<PortRef> Sources { get; init; }
}

/// <summary>Fork：一个输入 → 多路并行分支。</summary>
public sealed class ForkSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Fork;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.ControlFlow;

    /// <summary>派生输入端口。</summary>
    public required PortRef Source { get; init; }

    /// <summary>并行分支目标节点。</summary>
    public required IReadOnlyList<string> Branches { get; init; }
}

/// <summary>Join：并行分支汇合 → 同步点（barrier）。</summary>
public sealed class JoinSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Join;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.ControlFlow;

    /// <summary>参与同步的多个来源端口。</summary>
    public required IReadOnlyList<PortRef> Sources { get; init; }

    /// <summary>汇合后释放到的目标节点。</summary>
    public required string TargetNodeId { get; init; }
}