using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// KeyMapping 逻辑的节点库承载（三库收敛：ur_keymappings 并入 ur_nodes，NodeType=<see cref="NodeTypes.KeyMapping"/>）。
/// 实现 <see cref="IKeyMappingStore"/>，读写经 <see cref="INodeStore"/> 落为 keymapping 节点：
/// NodeKey=<see cref="KeyMappingKeys.StoreNodeKey"/>（keymapping.{category}.{nodeLevelKey}）、Key=分类 + 节点级键、
/// PayloadJson=映射 JSON。省去独立 ur_keymappings 表。键方案单一权威见 <see cref="KeyMappingKeys"/>。
/// </summary>
public sealed class NodeKeyMappingStore : IKeyMappingStore
{
    private readonly INodeStore _nodes;

    public NodeKeyMappingStore(INodeStore nodes)
    {
        _nodes = nodes ?? throw new System.ArgumentNullException(nameof(nodes));
    }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <param name="key">节点级 KeyMapping 规范键（<see cref="KeyMappingKeys.NodeLevelKey"/>，即 {workflowId}.{nodeKey}）。</param>
    public Task SaveAsync(KeyMappingCategory category, string key, string valueJson, CancellationToken ct = default)
        => _nodes.PutAsync(new NodeRecord
        {
            NodeKey = KeyMappingKeys.StoreNodeKey(category, key),
            NodeType = NodeTypes.KeyMapping,
            Key = KeyMappingKeys.StoreKey(category, key),
            PayloadJson = valueJson ?? "{}",
        }, ct);
}
