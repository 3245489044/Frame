using System;
using System.Collections.Generic;

namespace UniversalWorkflowFramework.Relations.Kernel;

/// <inheritdoc />
public sealed class RelationOperationResolver : IRelationOperationResolver
{
    private readonly RelationPlanCatalog _catalog;
    private readonly IRelationPolicy _policy;

    public RelationOperationResolver(RelationPlanCatalog catalog, IRelationPolicy policy)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <inheritdoc />
    public RelationOperation? Resolve(string operation) => _catalog.Resolve(operation);

    /// <inheritdoc />
    public RelationOperation? ResolveByEdge(string edge, bool remove) => _catalog.ResolveByEdge(edge, remove);

    /// <inheritdoc />
    public RelationDerivation? ResolveDerivation(string deriveId) => _catalog.ResolveDerivation(deriveId);

    /// <inheritdoc />
    public bool IsDeclaredEdge(string edge)
        => !string.IsNullOrWhiteSpace(edge) && _policy.IsDeclared(edge);

    /// <inheritdoc />
    public bool IsDataflowEdge(string edge)
        => !string.IsNullOrWhiteSpace(edge) && DataflowEdges.Contains(edge);

    /// <summary>
    /// 数据流边白名单（声明图/数据流语义；不走 <c>[Relation]</c> 归属写）。
    /// <para>归属用法（裸 refType + task 源）必须消灭，仅保留 provenance/键映射语义。</para>
    /// </summary>
    private static readonly HashSet<string> DataflowEdges = new(StringComparer.Ordinal)
    {
        "node_to_node",
        "keymapping_to_node",
    };
}
