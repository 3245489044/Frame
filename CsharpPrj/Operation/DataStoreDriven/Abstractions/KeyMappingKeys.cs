namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// KeyMapping 键方案的单一权威（三库收敛：KeyMapping 属于节点逻辑）。
/// 两级键：
///   · 节点级规范键 <see cref="NodeLevelKey"/> = {workflowId}.{nodeKey} —— 图库 KeyMapping 资源 Id / KeyMappingRef、
///     以及 NodeExecutionService 传给 <see cref="IKeyMappingStore.SaveAsync"/> 的 key 均用此键（图库与写路径同一键，消除两处硬编码漂移）。
///   · 节点库存储键 <see cref="StoreNodeKey"/> = keymapping.{category}.{nodeLevelKey} —— NodeType=keymapping 节点的 NodeKey。
/// 二者是同一规范键的分类派生（图库按节点聚合 input/output 为一类，节点库按 category 分两条记录），由此处统一推导，
/// 避免图库（NodeRelationFactory）与节点库（NodeKeyMappingStore）各自拼串导致的两套键不一致。
/// </summary>
public static class KeyMappingKeys
{
    /// <summary>节点级 KeyMapping 规范键（权威）：{workflowId}.{nodeKey}。</summary>
    public static string NodeLevelKey(string workflowId, string nodeKey) => $"{workflowId}.{nodeKey}";

    /// <summary>节点库存储键（NodeType=keymapping）：keymapping.{category}.{nodeLevelKey}。</summary>
    public static string StoreNodeKey(KeyMappingCategory category, string nodeLevelKey) => NodeKeyPrefix.KeyMapping + category + "." + nodeLevelKey;

    /// <summary>节点库 Key 列（分类 + 节点级键）：{category}.{nodeLevelKey}。</summary>
    public static string StoreKey(KeyMappingCategory category, string nodeLevelKey) => $"{category}.{nodeLevelKey}";
}
