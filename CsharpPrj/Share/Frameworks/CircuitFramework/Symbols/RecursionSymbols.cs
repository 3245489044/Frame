using Share.CircuitFramework.Core;

namespace Share.CircuitFramework.Symbols;

/// <summary>递归展开策略：执行器如何展开回边。</summary>
public enum RecursionStrategy
{
    /// <summary>栈式深递归。</summary>
    Stacked,

    /// <summary>尾递归-迭代。</summary>
    Iterative,

    /// <summary>记忆化（防指数爆炸，如 Fibonacci）。</summary>
    Memoized,
}

/// <summary>
/// BaseCase：递归终止条件声明（必须显式存在，否则编译拒绝）。
/// 递归从"隐式控制流"变为"显式声明符号"，策略交给执行引擎，业务图只声明"这里有回环 + 终止条件"。
/// </summary>
public sealed class BaseCaseSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.BaseCase;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.Recursion;

    /// <summary>判停布尔谓词（可作为表达式文本或引用某个判定节点端口）。</summary>
    public required string Predicate { get; init; }

    /// <summary>终止时的输出（端口 => 值）。</summary>
    public required IReadOnlyDictionary<string, string> Emit { get; init; }
}

/// <summary>
/// Recursion：圈内回边声明——某输出回连到本电路（或祖先）的输入。
/// 这是图上唯一允许成环的边类型；必须携带 BaseCase，否则编译失败。
/// </summary>
public sealed class RecursionSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Recursion;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.Recursion;

    /// <summary>承载该递归的节点。</summary>
    public required string NodeId { get; init; }

    /// <summary>回边源：某输出端口。</summary>
    public required PortRef BackEdge { get; init; }

    /// <summary>回连端口：目标输入端口。</summary>
    public required PortRef LoopBack { get; init; }

    /// <summary>基例终止条件（必须显式存在，否则编译拒绝）。</summary>
    public required BaseCaseSymbol BaseCase { get; init; }

    /// <summary>展开策略。</summary>
    public RecursionStrategy Strategy { get; init; } = RecursionStrategy.Stacked;
}

/// <summary>RecursionStrategy：给一个 Recursion 显式指定展开策略（等价 RecursionSymbol.Strategy 的独立声明承载）。</summary>
public sealed class RecursionStrategySymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.RecursionStrategy;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.Recursion;

    /// <summary>所属 Recursion 承载节点。</summary>
    public required string NodeId { get; init; }

    /// <summary>展开策略。</summary>
    public required RecursionStrategy Strategy { get; init; }
}

/// <summary>Loop：有界重复（可计次循环），非回边而是折叠。</summary>
public sealed class LoopSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Loop;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.Recursion;

    /// <summary>承载该循环的节点。</summary>
    public required string NodeId { get; init; }

    /// <summary>重复次数。</summary>
    public required int Repeats { get; init; }

    /// <summary>被重复的子电路（内联）。</summary>
    public required Circuit Body { get; init; }
}

/// <summary>WhileCond：上/后判定条件循环。</summary>
public sealed class WhileCondSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.WhileCond;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.Recursion;

    /// <summary>承载该循环的节点。</summary>
    public required string NodeId { get; init; }

    /// <summary>上判定（每次执行前判停）条件。</summary>
    public required string Condition { get; init; }

    /// <summary>后判定条件（可选，用后判定代替上判定）。</summary>
    public string? PostCondition { get; init; }
}