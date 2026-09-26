using Modules.Parsing.Keys;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 节点库存储键的单一权威（三库收敛：config/checkpoint/keymapping/relation 均入统一节点库 ur_nodes，
/// 以 <see cref="NodeTypes"/> 判别 + NodeKey 前缀隔离语义）。
/// <para>
/// 键面收敛（2026-09-26）：四个键族的段序 / 分隔符 / 构造 / 反解单源于这里的 <see cref="KeyGrammar"/> 文法表
/// （内核 = <c>Modules.Parsing.Keys</c>）；<c>NodeConfigStore</c> / <c>NodeCheckpointStore</c> /
/// <c>KeyMappingKeys</c> / <c>RelationDeclarationKeys</c> / <c>ZoneNodeAdapter</c> 一律经 <c>Build*</c> 构造，
/// 禁再各自拼串（原分散的 <c>Prefix + … + "." + …</c> 拼接已消除）。
/// </para>
/// </summary>
public static class NodeKeyPrefix
{
    /// <summary>配置即数据行节点键文法（<c>config.{key}</c>）。</summary>
    public static readonly KeyGrammar ConfigGrammar =
        KeyGrammar.Create("nodes.config", "config.{key}", familyPrefix: "config.");

    /// <summary>checkpoint 节点键文法（<c>checkpoint.{threadId}.{ns}.{checkpointId}</c>）。</summary>
    public static readonly KeyGrammar CheckpointGrammar =
        KeyGrammar.Create("nodes.checkpoint", "checkpoint.{threadId}.{ns}.{checkpointId}", familyPrefix: "checkpoint.");

    /// <summary>KeyMapping 节点键文法（<c>keymapping.{category}.{nodeLevelKey}</c>）。</summary>
    public static readonly KeyGrammar KeyMappingGrammar =
        KeyGrammar.Create("nodes.keymapping", "keymapping.{category}.{nodeLevelKey}", familyPrefix: "keymapping.");

    /// <summary>声明关系节点键文法（<c>relations.{workflowId}.{relationId}</c>）。</summary>
    public static readonly KeyGrammar RelationsGrammar =
        KeyGrammar.Create("nodes.relations", "relations.{workflowId}.{relationId}", familyPrefix: "relations.");

    /// <summary>节点库四键族注册表（读侧归属反解唯一入口；歧义/未命中即诊断可见化）。</summary>
    public static readonly KeyGrammarRegistry Registry = CreateRegistry();

    /// <summary>配置即数据行节点键前缀（config.{key}）。</summary>
    public static string Config => ConfigGrammar.FamilyPrefix;

    /// <summary>checkpoint 节点键前缀（checkpoint.{threadId}.{ns}.{checkpointId}）。</summary>
    public static string Checkpoint => CheckpointGrammar.FamilyPrefix;

    /// <summary>KeyMapping 节点键前缀（keymapping.{category}.{nodeLevelKey}）。</summary>
    public static string KeyMapping => KeyMappingGrammar.FamilyPrefix;

    /// <summary>声明关系节点键前缀（relations.{workflowId}.{relationId}）。</summary>
    public static string Relations => RelationsGrammar.FamilyPrefix;

    /// <summary>配置即数据行节点键（<c>config.{key}</c>）。</summary>
    public static string BuildConfig(string key) => ConfigGrammar.BuildLenient(key);

    /// <summary>checkpoint 节点键（<c>checkpoint.{threadId}.{ns}.{checkpointId}</c>）。</summary>
    public static string BuildCheckpoint(string threadId, string? ns, string checkpointId)
        => CheckpointGrammar.BuildLenient(threadId, ns, checkpointId);

    /// <summary>KeyMapping 节点键（<c>keymapping.{category}.{nodeLevelKey}</c>）。</summary>
    public static string BuildKeyMapping(string category, string nodeLevelKey)
        => KeyMappingGrammar.BuildLenient(category, nodeLevelKey);

    /// <summary>声明关系节点键（<c>relations.{workflowId}.{relationId}</c>）。</summary>
    public static string BuildRelation(string workflowId, string relationId)
        => RelationsGrammar.BuildLenient(workflowId, relationId);

    private static KeyGrammarRegistry CreateRegistry()
    {
        var registry = new KeyGrammarRegistry();
        registry.Register(ConfigGrammar);
        registry.Register(CheckpointGrammar);
        registry.Register(KeyMappingGrammar);
        registry.Register(RelationsGrammar);
        return registry;
    }
}
