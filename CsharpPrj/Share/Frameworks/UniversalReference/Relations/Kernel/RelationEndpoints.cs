using UniversalWorkflowFramework.Core.Models;

namespace Operation.DataStoreDriven.Relations;

/// <summary>关系端点工厂（唯一构造点）：类型 + 裸键 + 作用域 → <see cref="ResourceIdentifier"/>。</summary>
public static class RelationEndpoints
{
    public const string NodeType = "node";

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
