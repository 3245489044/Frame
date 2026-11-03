using System.Threading;
using System.Threading.Tasks;

namespace UniversalWorkflowFramework.Relations.Kernel;

/// <summary>未装配策略（plan 编译失败时的显式占位）：一切边均视为未声明/未废弃。</summary>
public sealed class UnassembledRelationPolicy : IRelationPolicy
{
    public static readonly UnassembledRelationPolicy Instance = new();

    public RelationEdgeSpec? FindEdge(string edgeName) => null;

    public RelationConstraintSpec? FindConstraint(string edgeName) => null;

    public bool IsDeclared(string edgeName) => false;

    public bool IsDeprecated(string edgeName) => false;
}

/// <summary>未装配探针（显式命名，禁默认值伪装事实）：恒返回 <c>false</c>。</summary>
public sealed class UnassembledEntityExistenceProbe : IEntityExistenceProbe
{
    public static readonly UnassembledEntityExistenceProbe Instance = new();

    public Task<bool> ExistsAsync(string entityType, string id, CancellationToken ct = default)
        => Task.FromResult(false);

    public bool IsMapped(string entityType) => false;
}
