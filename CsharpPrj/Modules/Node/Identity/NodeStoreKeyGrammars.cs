using Modules.Parsing.Keys;

namespace Modules.Node.Identity;

/// <summary>
/// 节点库键族的**文法声明单源**（收编；D1-② 口径）：config / checkpoint / keymapping / relations /
/// session-message 五族的「段序 + 分隔符 + 族前缀 + 兼容模板 + 键序」一律在此声明；
/// 词法、正反解、族判据由键文法内核 <see cref="KeyGrammar"/> 提供（本工程禁自建第二套正则）。
/// <para>
/// 分工：**本类只承载键模板事实**（不含领域语义）。领域语义（如 ordinal 轮内重置、存量旧键容忍、
/// 时序比较口径）留在上层域类（如 <c>SessionMessageKeys</c>）—— 二者以「本类提供文法实例、
/// 域类提供语义」相接，避免语义下沉进位置层。
/// </para>
/// <para>
/// 载体无关：键文法只描述键空间，不描述落在哪个容器/介质（位置归 <c>PlacementSpec</c> +
/// <c>IPlacementResolver</c> 反解，禁在本层写 loc→carrier 映射）。
/// </para>
/// </summary>
public static class NodeStoreKeyGrammars
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

    /// <summary>
    /// 会话消息节点键文法（<c>session.{container}.r{round}.{kind}.{ordinal}</c>；兼容模板只读反解存量键）。
    /// </summary>
    /// <remarks>
    /// 轮次段为必需项：<c>ordinal</c> 是轮内序号（每轮从 1 重置），无轮次段则跨轮同名键相同 ⇒
    /// 落库 upsert 会覆盖上一轮消息。兼容模板沿用同一占位名 <c>ordinal</c>，
    /// 使新旧键共用同一 <c>orderKeys</c>（排序口径单一，无第二套序）。
    /// </remarks>
    public static readonly KeyGrammar SessionMessageGrammar = KeyGrammar.Create(
        "session-message",
        "session.{container}.r{round:number}.{kind}.{ordinal:number}",
        familyPrefix: "session.",
        legacyTemplates: new[]
        {
            "session.{container}.r{round:number}.msg.{ordinal:number}",
            "session.{container}.msg.{ordinal:number}",
        },
        orderKeys: new[] { "round", "ordinal" });

    /// <summary>节点库键族文法注册表（读侧归属反解唯一入口；歧义/未命中即诊断可见化）。</summary>
    public static readonly KeyGrammarRegistry Registry = CreateRegistry();

    /// <summary>配置即数据行节点键前缀（config.{key}）；<c>static readonly</c>（非可变静态面，K5）。</summary>
    public static readonly string Config = ConfigGrammar.FamilyPrefix;

    /// <summary>checkpoint 节点键前缀（checkpoint.{threadId}.{ns}.{checkpointId}）。</summary>
    public static readonly string Checkpoint = CheckpointGrammar.FamilyPrefix;

    /// <summary>KeyMapping 节点键前缀（keymapping.{category}.{nodeLevelKey}）。</summary>
    public static readonly string KeyMapping = KeyMappingGrammar.FamilyPrefix;

    /// <summary>声明关系节点键前缀（relations.{workflowId}.{relationId}）。</summary>
    public static readonly string Relations = RelationsGrammar.FamilyPrefix;

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
