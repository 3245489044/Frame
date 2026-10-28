using UniversalWorkflowFramework.Relations;
using Plate.DslMethod.Schema;
using Usharp.Semantics;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// 边策略的编译产物实现（<see cref="IRelationPolicy"/>）：边/约束取自同一次 Usharp 编译的
/// <c>[Edge]</c>/<c>[Constraint]</c> 声明（与关系计划同源，禁第二份词表）。
/// </summary>
public sealed class CompilationRelationPolicy : IRelationPolicy
{
    private readonly Dictionary<string, ReferenceEdgeType> _edges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReferenceConstraint> _constraints = new(StringComparer.Ordinal);

    public CompilationRelationPolicy(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        foreach (var declaration in compilation.Declarations.Edges)
        {
            var edge = ReferenceEdgeType.From(declaration);
            if (edge is not null) _edges.TryAdd(edge.Name, edge);
        }

        foreach (var declaration in compilation.Declarations.OfKind(Plate.DslMethod.DslSymbolKind.Constraint))
        {
            var constraint = ReferenceConstraint.From(declaration);
            if (constraint is not null) _constraints.TryAdd(constraint.Edge, constraint);
        }
    }

    /// <inheritdoc />
    public RelationEdgeSpec? FindEdge(string edgeName)
        => !string.IsNullOrWhiteSpace(edgeName) && _edges.TryGetValue(edgeName, out var edge)
            ? new RelationEdgeSpec(edge.Name, edge.Source, edge.Target, edge.Deprecated)
            : null;

    /// <inheritdoc />
    public RelationConstraintSpec? FindConstraint(string edgeName)
        => !string.IsNullOrWhiteSpace(edgeName) && _constraints.TryGetValue(edgeName, out var constraint)
            ? new RelationConstraintSpec(constraint.Edge, "declared")
            : null;

    /// <inheritdoc />
    public bool IsDeclared(string edgeName) => FindEdge(edgeName) is not null;

    /// <inheritdoc />
    public bool IsDeprecated(string edgeName) => FindEdge(edgeName)?.IsDeprecated == true;
}
