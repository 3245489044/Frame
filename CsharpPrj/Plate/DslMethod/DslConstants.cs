using Plate.DslMethod.Abstractions;

namespace Plate.DslMethod;

/// <summary>DSL 定义层：符号类型枚举。</summary>
public enum DslSymbolKind
{
    Axiom, Type, Def, Gate, Sm, Lo, Strategy, Sep, Dr, Bind,
    Profile, Pattern, Attention, Level,
    Param, Theorem, AntiPattern, Symbol, AxiomBlock,
    Node, Edge, Constraint,
    Template, Schema,
    // Ushar 语言层（Frame\Ushar）：空间 / 矩阵 / 流 / 引用（见 Ushar README「语言规范」）
    Space, Matrix, Flow, Ref,
    Unknown,
}

/// <summary>前缀注册条目：行前缀 → 符号类型 + STYP 缩写（[CR] 目标匹配用）。</summary>
public sealed record DslPrefixDef(string Prefix, DslSymbolKind Kind, string Stub);

/// <summary>
/// DSL 定义层：前缀注册表与契约维度常量。
/// 新增前缀 = 在此增条目（文法不变，对齐 dsl.md §4.5「新增 STYP = 增条目」扩展规则）。
/// </summary>
public static class DslConstants
{
    /// <summary>注册的 DSL 行前缀（含 clinerules STYP 与 [Bind] 契约符号）。</summary>
    public static readonly IReadOnlyList<DslPrefixDef> Prefixes = new DslPrefixDef[]
    {
        new("[AX]",       DslSymbolKind.Axiom,     "AX"),
        new("[AX-CORE]",  DslSymbolKind.Axiom,     "AX-CORE"),
        new("[AX-META]",  DslSymbolKind.Axiom,     "AX-META"),
        new("[AX-FRAME]", DslSymbolKind.Axiom,     "AX-FRAME"),
        new("[BAX]",      DslSymbolKind.Axiom,     "BAX"),
        new("[BAX-IDI]",  DslSymbolKind.Axiom,     "BAX-IDI"),
        new("[BAX-TIME]", DslSymbolKind.Axiom,     "BAX-TIME"),
        new("[BAX-C]",    DslSymbolKind.Axiom,     "BAX-C"),
        new("[BAX-D]",    DslSymbolKind.Axiom,     "BAX-D"),
        new("[BAX-X]",    DslSymbolKind.Axiom,     "BAX-X"),
        new("[Type]",     DslSymbolKind.Type,      "Type"),
        new("[Def]",      DslSymbolKind.Def,       "Def"),
        new("[Gate]",     DslSymbolKind.Gate,      "Gate"),
        new("[SM]",       DslSymbolKind.Sm,        "SM"),
        new("[LO]",       DslSymbolKind.Lo,        "LO"),
        new("[Strategy]", DslSymbolKind.Strategy,  "Strategy"),
        new("[SEP]",      DslSymbolKind.Sep,       "SEP"),
        new("[DR]",       DslSymbolKind.Dr,        "DR"),
        new("[Bind]",     DslSymbolKind.Bind,      "Bind"),
        new("[Profile]",  DslSymbolKind.Profile,   "Profile"),
        new("[Pattern]",  DslSymbolKind.Pattern,   "Pattern"),
        new("[Attention]",DslSymbolKind.Attention, "Attention"),
        new("[Level]",    DslSymbolKind.Level,     "Level"),
        // UniversalReference 引用 Schema 词汇表：资源节点类型 / 引用类型 / 配对约束
        new("[Node]",       DslSymbolKind.Node,       "Node"),
        new("[Edge]",       DslSymbolKind.Edge,       "Edge"),
        new("[Constraint]", DslSymbolKind.Constraint, "Constraint"),
        // Agent 储存模板 DSL：模板 = 数据点直接储存契约（表 / 模式 / 解析列 / 保留 / 衰减 / 预算），无价值判定
        new("[Template]",   DslSymbolKind.Template,   "Template"),
        // 结构化 Schema 声明（format.system.md 等；字段/必填/枚举），对齐 clinerules dsl.md §4.5
        new("[Schema]",     DslSymbolKind.Schema,     "Schema"),
        // Ushar 语言层四前缀（Frame\Ushar）：Space=坐标空间 / Matrix=空间上的一次变换 / Flow=链与工作流 / Ref=带空间归属的引用
        new("[Space]",      DslSymbolKind.Space,      "Space"),
        new("[Matrix]",     DslSymbolKind.Matrix,     "Matrix"),
        new("[Flow]",       DslSymbolKind.Flow,       "Flow"),
        new("[Ref]",        DslSymbolKind.Ref,        "Ref"),
    };

    /// <summary>按前缀查注册条目（未注册返回 null）。</summary>
    public static DslPrefixDef? Find(string prefix)
    {
        foreach (var p in Prefixes)
            if (string.Equals(p.Prefix, prefix, StringComparison.Ordinal)) return p;
        return null;
    }
}

/// <summary>契约维度常量：模块簇 / 轴 / 关系种类 / 状态 / 层级。</summary>
public static class ContractDimensions
{
    /// <summary>实现模块簇（对应代码目录，module 字段取值集合）。</summary>
    public static readonly string[] Modules =
    {
        "engine.loopframework",
        "agent.core.loops",
        "agent.core.boundaries",
        "agent.core.prompts",
        "agent.core.infrastructure",
        "agent.l0.contracts",
    };

    /// <summary>公理轴（axis 字段取值集合）。</summary>
    public static readonly string[] Axes =
    {
        "termination", "error", "round", "boundary", "output", "infra",
    };

    /// <summary>绑定关系种类（kind 字段取值集合）：单一事实源 = <see cref="BindKind"/>（implement/derive/guard）。</summary>
    public static readonly string[] Kinds = Enum.GetNames<BindKind>().Select(n => n.ToLowerInvariant()).ToArray();

    /// <summary>绑定状态（status 字段取值集合）：单一事实源 = <see cref="BindStatus"/>（aligned/gap/drift）。</summary>
    public static readonly string[] Statuses = Enum.GetNames<BindStatus>().Select(n => n.ToLowerInvariant()).ToArray();
}
