using Operation.DataStoreDriven.Relations;
using Plate.DslMethod.Schema;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// 边策略的声明驱动实现（<see cref="IRelationPolicy"/>）：唯一读数 = <c>reference.ushar</c> 编译出的
/// <see cref="ReferenceSchema"/>（禁在 C# 内联边策略 if）。
/// </summary>
public sealed class DeclaredRelationPolicy : IRelationPolicy
{
    private readonly Dictionary<string, ReferenceEdgeType> _edges;
    private readonly Dictionary<string, ReferenceConstraint> _constraints;

    public DeclaredRelationPolicy(ReferenceSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        _edges = new Dictionary<string, ReferenceEdgeType>(StringComparer.Ordinal);
        foreach (var edge in schema.Edges)
            _edges.TryAdd(edge.Name, edge);

        _constraints = new Dictionary<string, ReferenceConstraint>(StringComparer.Ordinal);
        foreach (var constraint in schema.Constraints)
            _constraints.TryAdd(constraint.Edge, constraint);
    }

    /// <inheritdoc />
    public ReferenceEdgeType? FindEdge(string edgeName)
        => !string.IsNullOrWhiteSpace(edgeName) && _edges.TryGetValue(edgeName, out var edge) ? edge : null;

    /// <inheritdoc />
    public ReferenceConstraint? FindConstraint(string edgeName)
        => !string.IsNullOrWhiteSpace(edgeName) && _constraints.TryGetValue(edgeName, out var constraint)
            ? constraint
            : null;

    /// <inheritdoc />
    public bool IsDeclared(string edgeName) => FindEdge(edgeName) is not null;

    /// <inheritdoc />
    public bool IsDeprecated(string edgeName) => FindEdge(edgeName)?.Deprecated == true;
}
