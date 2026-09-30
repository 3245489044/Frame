using Share.CircuitFramework.Core;

namespace Share.CircuitFramework.Symbols;

/// <summary>引用关系符号的五种语义。</summary>
public enum ReferenceRelation
{
    /// <summary>归属关系（父含子）。</summary>
    Owns,

    /// <summary>调用关系（等价 Call 的图表达）。</summary>
    Calls,

    /// <summary>弱依赖（须先就绪）。</summary>
    Depends,

    /// <summary>提供（对外暴露能力/端口）。</summary>
    Provides,

    /// <summary>消费（接收按名前缀/端口的供给）。</summary>
    Consumes,
}

/// <summary>
/// 引用关系符号（唯一宿主铁律）：Owns/Calls/Depends/Provides/Consumes 统一落到同一张图，一物一源。
/// ReferenceGraphCoordinator / ReferenceGrounding / 四域边语义 / WorkflowReferenceStaticizer /
/// RoundSpec / Contracts.json 中的引用关系，必须投影/迁移为本符号，不得各自再维护一份隐式关系。
/// </summary>
public sealed class ReferenceSymbol : CircuitSymbol
{
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.Reference;

    /// <summary>本符号的种类（按 ReferenceRelation 映射）。</summary>
    public override CircuitSymbolKind Kind => Relation switch
    {
        ReferenceRelation.Owns => CircuitSymbolKind.Owns,
        ReferenceRelation.Calls => CircuitSymbolKind.Calls,
        ReferenceRelation.Depends => CircuitSymbolKind.Depends,
        ReferenceRelation.Provides => CircuitSymbolKind.Provides,
        ReferenceRelation.Consumes => CircuitSymbolKind.Consumes,
        _ => throw new ArgumentOutOfRangeException(),
    };

    /// <summary>关系语义。</summary>
    public required ReferenceRelation Relation { get; init; }

    /// <summary>源资源标识（跨版本唯一，如 "doc.workflow.order#v3"）。</summary>
    public required string Source { get; init; }

    /// <summary>目标资源标识。</summary>
    public required string Target { get; init; }

    /// <summary>原始边字面量（来自 ReferenceEdgeTypes 或域别名）。</summary>
    public string? EdgeLiteral { get; init; }

    public override string ToString() => $"{Source} --{EdgeLiteral ?? Kind.ToString()}--> {Target}";
}