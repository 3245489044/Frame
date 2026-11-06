using UniversalWorkflowFramework.Core.Models;

namespace UniversalWorkflowFramework.Relations.Kernel;

/// <summary>关系端点工厂（唯一构造点）：类型 + 裸键 + 作用域 → <see cref="ResourceIdentifier"/>。</summary>
public static class RelationEndpoints
{
    public const string NodeType = "node";

    /// <summary>流资源类型（UR 既有词汇表成员）。</summary>
    public const string WorkflowType = "workflow";

    /// <summary>未登记端点的兜底资源类型（避免丢边）。</summary>
    public const string ResourceType = "resource";

    public static ResourceIdentifier Create(string entityType, string id, string? location = null)
        => new()
        {
            ResourceType = entityType,
            Id = id,
            Location = string.Equals(entityType, NodeType, StringComparison.Ordinal) ? location : null,
        };

    public static string KeyOf(ResourceIdentifier id)
        => $"{id.ResourceType}:{id.Id}";

    public static bool SameEntity(ResourceIdentifier left, ResourceIdentifier right)
        => string.Equals(KeyOf(left), KeyOf(right), StringComparison.Ordinal);
}
