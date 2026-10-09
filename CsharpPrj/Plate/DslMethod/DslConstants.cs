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
    // Ushar 第五语言单元：观测声明（作用域 / 实体 / 事件；见 Ushar README「观测接入」）
    Obs,
    // Ushar 控制流语言单元（R3）：分支 / 多路分派 / 调用（声明式控制流；见 Usharp README「控制流接入」）
    Branch, Switch, Call,
    // Ushar 区域语言单元（R3+）：区域声明（父层单节点 + 子计划迭代；区域 → 电路子电路）
    Region,
    // Ushar 容错/门控语言单元（R3+）：受控边（When）/ 受护作用域（Guard）
    When, Guard,
    // Ushar 域定义语言单元：节点域定义面（identity / placements / selections / retentions；见 Usharp README「域定义」）
    Domain,
    // Ushar 聚拢声明语言单元：[Gather] 收集端口（族 / 塑形词表；见 U2 落地方案 §1）
    Gather,
    Unknown,
}

/// <summary>前缀注册条目：行前缀 → 符号类型 + STYP 缩写（[CR] 目标匹配用）。</summary>
public sealed record DslPrefixDef(string Prefix, DslSymbolKind Kind, string Stub);

/// <summary>
/// DSL 定义层：框架核心前缀表与契约维度常量。
/// <para>
/// 本表是<b>框架核心前缀集</b>（通用 DSL 词汇，<b>不含任何领域前缀</b>）；领域前缀由各领域包在自身程序集内
/// 声明为 <see cref="Parsing.DslPrefixSet"/> 并注册到 <see cref="Parsing.DslPrefixRegistry"/>
/// （如 Usharp 的 <c>[Space]</c>/<c>[Matrix]</c>/<c>[Flow]</c>/<c>[Ref]</c>/<c>[Obs]</c>）。
/// 新增核心前缀 = 在此增条目（文法不变，对齐 dsl.md §4.5「新增 STYP = 增条目」扩展规则）。
/// </para>
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
        // 注：Usharp 五前缀（[Space]/[Matrix]/[Flow]/[Ref]/[Obs]）不在此核心表 —— 由 Usharp 领域包自持并
        //     注册到 DslPrefixRegistry（见 Frame\Usharp\src\Usharp.Syntax\UsharpPrefixes.cs）。
    };

    /// <summary>按前缀查核心表条目（未注册返回 null；领域前缀请用 <see cref="Parsing.DslPrefixSet.Find"/>）。</summary>
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
