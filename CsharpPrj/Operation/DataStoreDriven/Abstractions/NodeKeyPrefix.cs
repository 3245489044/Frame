namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 节点库存储键前缀的单一权威（三库收敛：config/checkpoint/keymapping/relation 均入统一节点库 ur_nodes，
/// 以 <see cref="NodeTypes"/> 判别 + NodeKey 前缀隔离语义）。收敛 NodeConfigStore / NodeCheckpointStore /
/// RelationDeclarationKeys / ReferenceGrounding / KeyMappingKeys 分散的字符串字面量，消除拼串漂移
/// （对齐 SingleImplementation / KeyMappingKeys 的「键方案单一权威」模式，并推广到全部节点键前缀）。
/// </summary>
public static class NodeKeyPrefix
{
    /// <summary>配置即数据行节点键前缀（config.{key}）。</summary>
    public const string Config = "config.";

    /// <summary>checkpoint 节点键前缀（checkpoint.{threadId}.{ns}.{checkpointId}）。</summary>
    public const string Checkpoint = "checkpoint.";

    /// <summary>KeyMapping 节点键前缀（keymapping.{category}.{nodeLevelKey}）。</summary>
    public const string KeyMapping = "keymapping.";

    /// <summary>声明关系节点键前缀（relations.{workflowId}.{relationId}）。</summary>
    public const string Relations = "relations.";
}
