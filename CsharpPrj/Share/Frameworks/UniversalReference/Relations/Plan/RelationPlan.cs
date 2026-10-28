namespace UniversalWorkflowFramework.Relations;

/// <summary>关系操作声明（<c>[Relation]</c> 的中性运行期形态；边类型/端点/作用域的唯一来源）。</summary>
public sealed record RelationOperation(
    string Id,
    string? Display,
    string Edge,
    string Source,
    string Target,
    string Flow,
    string Namespace,
    IReadOnlyList<string> Exists,
    string Duplicate,
    string OnMissing,
    string Arity,
    string Intent,
    string? Emit,
    string Container = "",
    string OnMismatch = "",
    string Provision = "")
{
    /// <summary>是否存在 <c>exists=</c> 声明；缺省 = <c>src,tgt</c>（与声明面默认一致）。</summary>
    public bool Probes(string side)
        => Exists.Count == 0 || Exists.Contains(side, StringComparer.Ordinal);

    /// <summary>删除意图（唯一来源 = 声明面 <c>intent=</c>；禁由 operation 名/前缀推断）。</summary>
    public bool IsRemove => string.Equals(Intent, "remove", StringComparison.Ordinal);

    /// <summary>容器归属校验是否启用（<c>container=</c> 非空）。</summary>
    public bool ChecksContainer => !string.IsNullOrWhiteSpace(Container);

    /// <summary>容器不一致处置（<c>on_mismatch=reject|report</c>；缺省 reject）。</summary>
    public bool ContainerRejects => !string.Equals(OnMismatch, "report", StringComparison.Ordinal);
}

/// <summary>关系不变量声明（<c>[Invariant]</c> 的中性运行期形态）。</summary>
public sealed record RelationInvariant(
    string Id,
    string Kind,
    string Edge,
    string OnViolation,
    string? Scope);

/// <summary>派生成员集声明（<c>[Derive]</c> 的中性运行期形态；<c>Via</c> 序 = 路径序）。</summary>
public sealed record RelationDerivation(
    string Id,
    string From,
    string To,
    IReadOnlyList<string> Via,
    string Op,
    string Normalize,
    string Order,
    IReadOnlyList<string> Empty);

/// <summary>关系计划（Usharp 编译产物 → 中性 DTO；运行期内核唯一输入）。</summary>
public sealed record RelationPlan(
    string Version,
    IReadOnlyList<RelationOperation> Operations,
    IReadOnlyList<RelationInvariant> Invariants,
    IReadOnlyList<RelationDerivation> Derivations,
    IReadOnlyList<RelationBinding>? Bindings = null,
    IReadOnlyList<RelationProvision>? Provisions = null)
{
    /// <summary>空计划（声明面缺失时的显式占位；禁以空目录冒充已装配）。</summary>
    public static readonly RelationPlan Empty = new(
        "0", Array.Empty<RelationOperation>(), Array.Empty<RelationInvariant>(), Array.Empty<RelationDerivation>());
}

/// <summary>容器归属声明（<c>[Binding]</c> 中性形态；U4 起由语言层产出）。</summary>
public sealed record RelationBinding(
    string Id,
    string Target,
    string ContainerFrom,
    string OnMismatch);

/// <summary>端点镜像归属声明（<c>[Provision]</c> 中性形态）。</summary>
public sealed record RelationProvision(
    string Id,
    string Entity,
    string Owner);

/// <summary>关系计划目录（operation / derive / binding 的唯一解析点）。</summary>
public sealed class RelationPlanCatalog
{
    private readonly Dictionary<string, RelationOperation> _operations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RelationDerivation> _derivations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RelationBinding> _bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<RelationInvariant>> _invariantsByEdge;

    public RelationPlanCatalog(RelationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Plan = plan;

        foreach (var operation in plan.Operations)
            _operations.TryAdd(operation.Id, operation);
        foreach (var derivation in plan.Derivations)
            _derivations.TryAdd(derivation.Id, derivation);
        foreach (var binding in plan.Bindings ?? Array.Empty<RelationBinding>())
            _bindings.TryAdd(binding.Target, binding);

        _invariantsByEdge = plan.Invariants
            .GroupBy(i => i.Edge, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RelationInvariant>)g.ToList(), StringComparer.Ordinal);
    }

    public RelationPlan Plan { get; }

    public bool IsEmpty => _operations.Count == 0;

    public RelationOperation? Resolve(string operation)
        => !string.IsNullOrWhiteSpace(operation) && _operations.TryGetValue(operation, out var found) ? found : null;

    public RelationDerivation? ResolveDerivation(string deriveId)
        => !string.IsNullOrWhiteSpace(deriveId) && _derivations.TryGetValue(deriveId, out var found) ? found : null;

    /// <summary>按端点类型取容器归属声明（未声明 ⇒ null）。</summary>
    public RelationBinding? BindingFor(string target)
        => !string.IsNullOrWhiteSpace(target) && _bindings.TryGetValue(target, out var found) ? found : null;

    public IReadOnlyList<RelationInvariant> InvariantsFor(string edge)
        => _invariantsByEdge.TryGetValue(edge, out var found) ? found : Array.Empty<RelationInvariant>();

    public RelationOperation? ResolveByEdge(string edge, bool remove)
    {
        if (string.IsNullOrWhiteSpace(edge)) return null;
        var matches = _operations.Values
            .Where(o => string.Equals(o.Edge, edge, StringComparison.Ordinal) && o.IsRemove == remove)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
