namespace Share.CircuitFramework.Core;

/// <summary>
/// 符号的语义分类。覆盖全部逻辑：结构、数据流、控制流、循环/递归、容错、引用关系。
/// 每条符号是图上的一个一等对象，可被编译器识别、校验、变换。
/// </summary>
public enum CircuitSymbolKind
{
    // 结构符号（节点如何组织 / 调用链）
    Call,
    CalleeRef,
    Composite,
    SubCircuit,
    Entry,
    Exit,

    // 数据流符号（参数如何传递）
    Param,
    Literal,
    Fold,
    Unfold,
    Projection,

    // 控制流符号
    Sequence,
    Branch,
    Switch,
    Merge,
    Fork,
    Join,

    // 容错符号
    TryGuard,
    CatchRoute,
    Retry,
    Fallback,

    // 循环与递归符号（本框架创新点）
    Recursion,
    BaseCase,
    RecursionStrategy,
    Loop,
    WhileCond,

    // 引用关系符号（唯一宿主）
    Owns,
    Calls,
    Depends,
    Provides,
    Consumes,

    // 效果符号（G7：共享通道读写可见化）
    ReadsState,
    WritesState,
}

/// <summary>符号的语义大类，用于分组与编译器分派。</summary>
public enum CircuitSymbolCategory
{
    Structure,
    Dataflow,
    ControlFlow,
    FaultTolerance,
    Recursion,
    Reference,
    Effect,
}

/// <summary>
/// 电路符号基类。符号附带一个"承载节点/端口"或为电路级声明。
/// 符号只描述"怎么连、怎么流、怎么控"，不描述"算什么"（计算在节点内）。
/// </summary>
public abstract class CircuitSymbol
{
    /// <summary>符号种类。</summary>
    public abstract CircuitSymbolKind Kind { get; }

    /// <summary>符号语义大类。</summary>
    public abstract CircuitSymbolCategory Category { get; }
}