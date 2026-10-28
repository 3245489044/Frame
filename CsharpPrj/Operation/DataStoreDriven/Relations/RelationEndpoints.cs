using UniversalWorkflowFramework.Core.Models;

namespace Operation.DataStoreDriven.Relations;

/// <summary>
/// 关系端点工厂（唯一构造点）：类型 + 裸键 + 作用域 → <see cref="ResourceIdentifier"/>。
/// <para>作用域只落在 <c>node</c> 端点（与 P2「Location 进键、仅 node 携带 workflow」一致）；
/// 其它端点（task/history/session）为全局裸身份，禁再拼一次 Location。</para>
/// </summary>
public static class RelationEndpoints
{
    /// <summary>节点实体类型（唯一携带作用域的端点类型）。</summary>
    public const string NodeType = "node";

    /// <summary>构造端点（<paramref name="location"/> 仅对 <c>node</c> 生效）。</summary>
    public static ResourceIdentifier Create(string entityType, string id, string? location = null)
        => new()
        {
            ResourceType = entityType,
            Id = id,
            Location = string.Equals(entityType, NodeType, StringComparison.Ordinal) ? location : null,
        };

    /// <summary>端点的逻辑比较键（忽略 Location：跨作用域同一实体即同一端点）。</summary>
    public static string KeyOf(ResourceIdentifier id)
        => $"{id.ResourceType}:{id.Id}";

    /// <summary>同一实体判定（类型 + 裸键；忽略 Location）。</summary>
    public static bool SameEntity(ResourceIdentifier left, ResourceIdentifier right)
        => string.Equals(KeyOf(left), KeyOf(right), StringComparison.Ordinal);
}
