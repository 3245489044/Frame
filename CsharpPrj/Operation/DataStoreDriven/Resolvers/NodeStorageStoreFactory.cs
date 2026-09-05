using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Configuration;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using Operation.DataStoreDriven.Graph;
using Operation.DataStoreDriven.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Operation.DataStoreDriven.Resolvers
{
    /// <summary>
    /// 节点存储工厂（组合根）—— 装配「节点关系图存储 + KeyMapping 节点库承载」：
    /// <list type="bullet">
    ///   <item>节点关系存图库（唯一 owner=ReferenceGraphCoordinator，实现 INodeRelationStore + IResourceReferenceStore）</item>
    ///   <item><see cref="CreateNodeKeyMappings"/>：KeyMapping 三库收敛为节点库承载（NodeType=keymapping，ur_nodes）</item>
    /// </list>
    /// </summary>
    public static class NodeStorageStoreFactory
    {

        /// <summary>
        /// 三库收敛：KeyMapping 逻辑经节点库承载（NodeType=keymapping，ur_nodes），省去独立 ur_keymappings 表。
        /// 与节点库共用同一 <see cref="INodeStore"/>（LiteDB ur_nodes 或 Sqlite ur_nodes）。
        /// </summary>
        public static IKeyMappingStore CreateNodeKeyMappings(INodeStore nodes)
            => new NodeKeyMappingStore(nodes ?? throw new System.ArgumentNullException(nameof(nodes)));

        /// <summary>
        /// 节点存储装配期初始化（唯一入口）：建表/建图幂等触发。
        /// 与其余 DataStoreDriven store 一致，创建后必须调用（NodeKeyMappingStore 无独立建表，NodeStore 由 InitializeAsync 负责）。
        /// </summary>
        public static async Task InitializeAsync(
            INodeRelationStore? relations, IKeyMappingStore? keyMappings, CancellationToken ct = default)
        {
            if (relations is not null) await relations.InitializeAsync(ct).ConfigureAwait(false);
            if (keyMappings is not null) await keyMappings.InitializeAsync(ct).ConfigureAwait(false);
        }
    }
}
