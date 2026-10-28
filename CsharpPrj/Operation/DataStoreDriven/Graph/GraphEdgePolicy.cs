using Operation.DataStoreDriven.ReferenceSchema;

namespace Operation.DataStoreDriven.Graph;

/// <summary>边身份文法（<c>[Edge] identity=</c> 受控词）。</summary>
public enum EdgeIdentity
{
    /// <summary><c>{sourceKey}::{targetKey}::{refType}</c>（Location 不进边键）。</summary>
    SourceTargetRef = 0,

    /// <summary><c>{sourceKey}::{targetKey}::{refType}@{workflowId}</c>（Location 进边键；跨工作流不共享）。</summary>
    SourceTargetRefLocation = 1,
}

/// <summary>墓碑策略（<c>[Edge] tombstone=</c> 受控词）。</summary>
public enum TombstonePolicy
{
    /// <summary>不写墓碑（删除即遗忘，允许后续重建）。</summary>
    None = 0,

    /// <summary>写墓碑（删除意图持久；作为写前门）。</summary>
    Durable = 1,
}

/// <summary>复活来源（<c>[Edge] revive=</c> 受控词）。</summary>
public enum RevivePolicy
{
    /// <summary>仅显式声明（declare）可复活；业务/对账路径一律不复活。</summary>
    Explicit = 0,

    /// <summary>显式挂载（业务 role）可复活；对账/迁移不复活。</summary>
    Attach = 1,

    /// <summary>任何写入均拒绝（终态边）。</summary>
    Never = 2,
}

/// <summary>端点删除级联（<c>[Edge] cascade=</c> 受控词）。</summary>
public enum CascadePolicy
{
    /// <summary>连带删除关系声明。</summary>
    Declaration = 0,

    /// <summary>仅删除边（保留声明，供对账重建）。</summary>
    Edge = 1,

    /// <summary>不级联。</summary>
    None = 2,
}

/// <summary>写入角色（决定「写前查哪类墓碑」与「失败是否可见化」；与 <c>[Rule] graph-policy</c> 的 role 同词）。</summary>
public enum GraphMountRole
{
    /// <summary>业务接地（task 入队 / history attach / node link / UI 增删）。</summary>
    Business = 0,

    /// <summary>对账补边：只补缺失，不覆盖删除意图。</summary>
    Reconcile = 1,

    /// <summary>存量迁移：仅 DryRun/Apply 显式调用。</summary>
    Migration = 2,

    /// <summary>显式声明：唯一允许解墓碑的入口。</summary>
    Declare = 3,
}

/// <summary>写入门裁决（受控四态；对应规则表 <c>then.action</c>）。</summary>
public enum GraphWriteVerdict
{
    /// <summary>直接写入。</summary>
    Write = 0,

    /// <summary>先清墓碑再写入（显式复活）。</summary>
    ClearTombstoneThenWrite = 1,

    /// <summary>跳过写入并可见化（尊重删除意图）。</summary>
    SkipAndReport = 2,

    /// <summary>拒绝写入（终态边 / 未知角色）。</summary>
    Deny = 3,
}

/// <summary>写入意图（判据载体：结构事实 = 边类型 / 角色 / 墓碑是否存在 / 是否端点镜像 / 复活策略原文）。</summary>
public readonly record struct GraphWriteIntent(
    string RefType,
    GraphMountRole Role,
    bool TombstoneExists,
    bool Endpoint = false,
    string? Revive = null);

/// <summary>
/// 规则表裁决端口：由声明面 <c>Config/rules/graph-policy.rules.ushar</c> 驱动的唯一裁决实现（Agent 侧装配）。
/// <para>未注入 ⇒ <see cref="DeclaredGraphEdgePolicy"/> 走内置四态（零回归）；注入后规则表为真源。</para>
/// </summary>
public interface IGraphPolicyDecider
{
    /// <summary>按 role × tombstone × revive × kind 求值（首个命中；未命中 ⇒ <see cref="GraphWriteVerdict.Deny"/>）。</summary>
    GraphWriteVerdict Decide(GraphWriteIntent intent);
}

/// <summary>
/// 边策略端口（唯一策略读取点）：身份/墓碑/唯一/复活/级联全部来自 <c>[Edge]</c> 声明，C# 不内联 if。
/// <para>声明注入 = <c>IReferenceTypeRegistry</c>（编译产物 reference-schema）；未装配 ⇒ 声明缺省值。</para>
/// </summary>
public interface IGraphEdgePolicy
{
    EdgeIdentity IdentityOf(string refType);

    TombstonePolicy TombstoneOf(string refType);

    bool UniqueOf(string refType);

    RevivePolicy ReviveOf(string refType);

    CascadePolicy CascadeOf(string refType);

    /// <summary>role × tombstone × revive 的裁决。</summary>
    GraphWriteVerdict Admit(GraphWriteIntent intent);
}

/// <summary>
/// 声明驱动的边策略（<c>[Edge]</c> → 运行时）：边名解析单源 = <see cref="GraphEdgeNames.Resolve"/>。
/// <para>未声明策略字段的边 ⇒ 声明缺省（tombstone=durable / revive=explicit / identity=source_target_ref /
/// unique=false / cascade=declaration）。</para>
/// </summary>
public sealed class DeclaredGraphEdgePolicy : IGraphEdgePolicy
{
    private readonly IReferenceTypeRegistry? _registry;
    private readonly IGraphPolicyDecider? _decider;

    public DeclaredGraphEdgePolicy(IReferenceTypeRegistry? registry, IGraphPolicyDecider? decider = null)
    {
        _registry = registry;
        _decider = decider;
    }

    public EdgeIdentity IdentityOf(string refType)
    {
        var text = GraphEdgeNames.PolicyField(_registry, refType, static e => e.Identity);
        return string.Equals(text, "source_target_ref_location", StringComparison.Ordinal)
            ? EdgeIdentity.SourceTargetRefLocation
            : EdgeIdentity.SourceTargetRef;
    }

    public TombstonePolicy TombstoneOf(string refType)
    {
        var text = GraphEdgeNames.PolicyField(_registry, refType, static e => e.Tombstone);
        return string.Equals(text, "none", StringComparison.Ordinal) ? TombstonePolicy.None : TombstonePolicy.Durable;
    }

    public bool UniqueOf(string refType) => GraphEdgeNames.FindEdge(_registry, refType)?.Unique ?? false;

    public RevivePolicy ReviveOf(string refType)
    {
        var text = GraphEdgeNames.PolicyField(_registry, refType, static e => e.Revive);
        return text switch
        {
            "attach" => RevivePolicy.Attach,
            "never" => RevivePolicy.Never,
            _ => RevivePolicy.Explicit,
        };
    }

    public CascadePolicy CascadeOf(string refType)
    {
        var text = GraphEdgeNames.PolicyField(_registry, refType, static e => e.Cascade);
        return text switch
        {
            "edge" => CascadePolicy.Edge,
            "none" => CascadePolicy.None,
            _ => CascadePolicy.Declaration, // 未声明 ⇒ 保持既有行为：删端点连带删声明
        };
    }

    /// <inheritdoc />
    public GraphWriteVerdict Admit(GraphWriteIntent intent)
    {
        // 规则表装载后为唯一裁决面（声明驱动）；未装载 ⇒ 内置四态（零回归）。
        if (_decider is not null) return _decider.Decide(intent);

        // 端点镜像：只允许显式声明复活（declare 清端点墓碑）；业务/对账一律尊重墓碑。
        if (intent.Endpoint)
        {
            if (!intent.TombstoneExists) return GraphWriteVerdict.Write;
            return intent.Role == GraphMountRole.Declare
                ? GraphWriteVerdict.ClearTombstoneThenWrite
                : GraphWriteVerdict.SkipAndReport;
        }

        var revive = ReviveOf(intent.RefType);
        if (revive == RevivePolicy.Never) return GraphWriteVerdict.Deny;
        if (!intent.TombstoneExists) return GraphWriteVerdict.Write;

        return intent.Role switch
        {
            GraphMountRole.Declare => GraphWriteVerdict.ClearTombstoneThenWrite,
            GraphMountRole.Business => revive == RevivePolicy.Attach
                ? GraphWriteVerdict.ClearTombstoneThenWrite   // 显式挂载可重建（revive=attach）
                : GraphWriteVerdict.SkipAndReport,             // revive=explicit：自动路径禁写
            GraphMountRole.Reconcile => GraphWriteVerdict.SkipAndReport,
            _ => GraphWriteVerdict.Deny,                       // Migration / 未知角色
        };
    }
}

/// <summary>边名解析单源（refType 可能携带端口/键映射编码；<c>[Edge]</c> 查表按主干名）。</summary>
public static class GraphEdgeNames
{
    /// <summary>复活策略受控词（用于写入意图的结构事实；与 <c>[Rule] when.revive</c> 同词）。</summary>
    public static string ReviveText(RevivePolicy policy) => policy switch
    {
        RevivePolicy.Attach => "attach",
        RevivePolicy.Never => "never",
        _ => "explicit",
    };

    /// <summary>解析边类型主干名（去除 <c>keymapping_to_node:</c> 后缀编码等）。</summary>
    public static string Resolve(string refType)
    {
        if (string.IsNullOrEmpty(refType)) return string.Empty;
        var i = refType.IndexOf(':');
        return i > 0 ? refType.Substring(0, i) : refType;
    }

    /// <summary>按主干名取边声明（未装配注册表 ⇒ null）。</summary>
    public static Plate.DslMethod.Schema.ReferenceEdgeType? FindEdge(IReferenceTypeRegistry? registry, string refType)
    {
        if (registry is null || string.IsNullOrWhiteSpace(refType)) return null;
        try { return registry.FindEdge(Resolve(refType)); }
        catch { return null; }
    }

    /// <summary>取某策略字段原文（未装配 / 未声明 ⇒ null）。</summary>
    public static string? PolicyField(
        IReferenceTypeRegistry? registry,
        string refType,
        Func<Plate.DslMethod.Schema.ReferenceEdgeType, string?> selector)
    {
        var edge = FindEdge(registry, refType);
        return edge is null ? null : selector(edge);
    }
}
