using System;
using Modules.Node.Relations;

namespace Operation.DataStoreDriven.Abstractions
{
    /// <summary>
    /// 声明关系入口（L4 领域语义；关系记录本体 <see cref="RelationDeclaration"/> /
    /// <see cref="RelationEndpoint"/> 已上移 L2 <c>Modules.Node.Relations</c>）：
    /// relationId（声明节点唯一键，幂等 upsert 依据）+ 声明内容。
    /// </summary>
    public sealed class RelationDeclarationEntry
    {
        public string RelationId { get; init; } = string.Empty;

        public RelationDeclaration Declaration { get; init; } = new();
    }

    /// <summary>声明关系节点的规范键 / Id 构造（NodeKey="relations.{workflowId}.{relationId}"，幂等 upsert 依据）。</summary>
    public static class RelationDeclarationKeys
    {
        /// <summary>声明节点规范键：{workflowId}.{relationId}（对齐 <see cref="KeyMappingKeys.NodeLevelKey"/> 的节点级规范键命名）。</summary>
        public static string RelationId(string workflowId, string relationId) => KeyMappingKeys.NodeLevelKey(workflowId, relationId);

        /// <summary>声明节点 NodeKey（ur_nodes 唯一键）：relations.{workflowId}.{relationId}。</summary>
        public static string NodeKey(string workflowId, string relationId) => NodeKeyPrefix.BuildRelation(workflowId, relationId);
    }
}
