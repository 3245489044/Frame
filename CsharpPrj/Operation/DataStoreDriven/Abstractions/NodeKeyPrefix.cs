using Modules.Node.Identity;
using Modules.Parsing.Keys;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 节点库存储键的**兼容别名门面**（键文法声明单源已上移 <see cref="NodeStoreKeyGrammars"/>，层 2）。
/// <para>
/// 本类**不含任何键字面量**：段序 / 分隔符 / 族前缀 / 兼容模板 / 键序一律取自
/// <see cref="NodeStoreKeyGrammars"/>（词法内核 = <c>Modules.Parsing.Keys</c>）；
/// <c>NodeConfigStore</c> / <c>NodeCheckpointStore</c> / <c>KeyMappingKeys</c> /
/// <c>RelationDeclarationKeys</c> / <c>ZoneNodeAdapter</c> 仍一律经 <c>Build*</c> 构造，禁各自拼串。
/// </para>
/// <para>保留门面 = 消费方零改动（别名转发一版；方案 §6-DS-P1 条款）。新增键族请直接扩 <see cref="NodeStoreKeyGrammars"/>。</para>
/// </summary>
public static class NodeKeyPrefix
{
    /// <summary>配置即数据行节点键文法（<c>config.{key}</c>）。</summary>
    public static readonly KeyGrammar ConfigGrammar = NodeStoreKeyGrammars.ConfigGrammar;

    /// <summary>checkpoint 节点键文法（<c>checkpoint.{threadId}.{ns}.{checkpointId}</c>）。</summary>
    public static readonly KeyGrammar CheckpointGrammar = NodeStoreKeyGrammars.CheckpointGrammar;

    /// <summary>KeyMapping 节点键文法（<c>keymapping.{category}.{nodeLevelKey}</c>）。</summary>
    public static readonly KeyGrammar KeyMappingGrammar = NodeStoreKeyGrammars.KeyMappingGrammar;

    /// <summary>声明关系节点键文法（<c>relations.{workflowId}.{relationId}</c>）。</summary>
    public static readonly KeyGrammar RelationsGrammar = NodeStoreKeyGrammars.RelationsGrammar;

    /// <summary>节点库四键族注册表（读侧归属反解唯一入口；歧义/未命中即诊断可见化）。</summary>
    public static KeyGrammarRegistry Registry => NodeStoreKeyGrammars.Registry;

    /// <summary>配置即数据行节点键前缀（config.{key}）。</summary>
    public static string Config => NodeStoreKeyGrammars.Config;

    /// <summary>checkpoint 节点键前缀（checkpoint.{threadId}.{ns}.{checkpointId}）。</summary>
    public static string Checkpoint => NodeStoreKeyGrammars.Checkpoint;

    /// <summary>KeyMapping 节点键前缀（keymapping.{category}.{nodeLevelKey}）。</summary>
    public static string KeyMapping => NodeStoreKeyGrammars.KeyMapping;

    /// <summary>声明关系节点键前缀（relations.{workflowId}.{relationId}）。</summary>
    public static string Relations => NodeStoreKeyGrammars.Relations;

    /// <summary>配置即数据行节点键（<c>config.{key}</c>）。</summary>
    public static string BuildConfig(string key) => NodeStoreKeyGrammars.BuildConfig(key);

    /// <summary>checkpoint 节点键（<c>checkpoint.{threadId}.{ns}.{checkpointId}</c>）。</summary>
    public static string BuildCheckpoint(string threadId, string? ns, string checkpointId)
        => NodeStoreKeyGrammars.BuildCheckpoint(threadId, ns, checkpointId);

    /// <summary>KeyMapping 节点键（<c>keymapping.{category}.{nodeLevelKey}</c>）。</summary>
    public static string BuildKeyMapping(string category, string nodeLevelKey)
        => NodeStoreKeyGrammars.BuildKeyMapping(category, nodeLevelKey);

    /// <summary>声明关系节点键（<c>relations.{workflowId}.{relationId}</c>）。</summary>
    public static string BuildRelation(string workflowId, string relationId)
        => NodeStoreKeyGrammars.BuildRelation(workflowId, relationId);
}
