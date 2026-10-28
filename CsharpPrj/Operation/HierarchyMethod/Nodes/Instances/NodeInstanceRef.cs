using System;
using System.Collections.Generic;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.HierarchyMethod.Nodes.Instances;

/// <summary>
/// 节点实例身份（F1 根治，唯一形态）：<c>{ContainerKey, WorkflowId, NodeKey, InstanceKey}</c>。
/// <para>
/// 存储键与图端点一律由本类型派生（<see cref="ToStorageKey"/> / <see cref="ToEndpointId"/>），
/// 裸 <c>NodeKey</c> 退化为「工作流内逻辑节点名」，不再作为跨任务记录主键 ——
/// 消除「不同任务写同一 cline / cline_execute 后写覆盖先写」。
/// </para>
/// <para>
/// 存储键文法 = <c>{NodeKey}@{InstanceKey}</c>（实例后缀，便于裸键兼容读：剥离 <c>@</c> 后即 legacy 键）。
/// <c>InstanceKey</c> 缺省 = 任务键；同任务并发/重放时为 <c>{taskKey}:{round}:{attempt}</c>。
/// </para>
/// </summary>
public sealed record NodeInstanceRef(
    string ContainerKey,
    string WorkflowId,
    string NodeKey,
    string InstanceKey)
{
    /// <summary>存储键分隔符（实例后缀；唯一权威 = <c>NodeKeyPlan.InstanceSeparator</c>，禁调用点手拼）。</summary>
    public const char InstanceSeparator = NodeKeyPlan.InstanceSeparator;

    /// <summary>构造实例身份（<paramref name="instanceKey"/> 空 ⇒ 任务键；两者皆空 ⇒ legacy 裸键）。</summary>
    public static NodeInstanceRef Create(string? containerKey, string? workflowId, string? nodeKey, string? instanceKey)
    {
        var node = nodeKey?.Trim() ?? string.Empty;
        var instance = string.IsNullOrWhiteSpace(instanceKey) ? containerKey?.Trim() ?? string.Empty : instanceKey.Trim();
        return new NodeInstanceRef(containerKey?.Trim() ?? string.Empty, workflowId?.Trim() ?? string.Empty, node, instance);
    }

    /// <summary>legacy 形态（无实例）：读面回退用，写入禁止。</summary>
    public static NodeInstanceRef FromLegacy(string? workflowId, string? nodeKey)
        => new(string.Empty, workflowId?.Trim() ?? string.Empty, nodeKey?.Trim() ?? string.Empty, string.Empty);

    /// <summary>是否有实例身份（false = legacy 裸键）。</summary>
    public bool HasInstance => InstanceKey.Length > 0;

    /// <summary>存储主键：<c>{NodeKey}@{InstanceKey}</c>；无实例 ⇒ 裸 <c>NodeKey</c>。</summary>
    public string ToStorageKey()
        => HasInstance ? NodeKey + InstanceSeparator + InstanceKey : NodeKey;

    /// <summary>图端点 Id：<c>{WorkflowId}.{StorageKey}</c>（与 <c>NodeRelationFactory.NodeId</c> 同形）。</summary>
    public string ToEndpointId()
        => WorkflowId.Length > 0 ? WorkflowId + "." + ToStorageKey() : ToStorageKey();

    /// <summary>存储键合成（唯一构造点；禁调用点手拼分隔符）。</summary>
    public static string StorageKeyOf(string nodeKey, string? instanceKey)
        => string.IsNullOrWhiteSpace(instanceKey) ? nodeKey : nodeKey + InstanceSeparator + instanceKey;

    /// <summary>是否实例存储键形态。</summary>
    public static bool IsInstanceStorageKey(string? storageKey)
        => !string.IsNullOrEmpty(storageKey) && storageKey.IndexOf(InstanceSeparator) > 0;

    /// <summary>剥离实例后缀（legacy 兼容读）：非实例键 ⇒ 原样返回。</summary>
    public static string StripInstance(string storageKey)
    {
        if (!IsInstanceStorageKey(storageKey)) return storageKey;
        return storageKey[..storageKey.IndexOf(InstanceSeparator)];
    }

    /// <summary>由存储键还原（无实例 ⇒ <see cref="FromLegacy"/> 形态）。</summary>
    public static NodeInstanceRef Parse(string? workflowId, string storageKey)
    {
        var key = storageKey ?? string.Empty;
        if (!IsInstanceStorageKey(key)) return FromLegacy(workflowId, key);
        var at = key.IndexOf(InstanceSeparator);
        return new NodeInstanceRef(string.Empty, workflowId?.Trim() ?? string.Empty, key[..at], key[(at + 1)..]);
    }
}

/// <summary>已落库节点引用（<see cref="NodePersistResult"/> 的元素；承载 NodeType 与存储键）。</summary>
public sealed record PersistedNodeRef(NodeInstanceRef Ref, string NodeType, string StorageKey);

/// <summary>未落库记录（原因可读；<c>Queued</c> = 已入 Outbox 尚未落盘，不构成失败）。</summary>
public sealed record NodePersistSkip(string NodeKey, string? InstanceKey, string Reason);

/// <summary>落库跳过原因词面（单源）。</summary>
public static class NodePersistSkipReasons
{
    /// <summary>记录 NodeKey 空白（脏记录）。</summary>
    public const string EmptyNodeKey = "empty-node-key";

    /// <summary>无任何非空输出端口（不造空节点）。</summary>
    public const string EmptyPorts = "empty-ports";

    /// <summary>写入抛错（单条隔离，不翻转本轮）。</summary>
    public const string WriteFailed = "write-failed";

    /// <summary>已入 Outbox（最终一致，不算失败）。</summary>
    public const string Queued = "queued";
}

/// <summary>
/// 轮次落库结果（I2：<c>attributed(round) ≡ persisted(round)</c>）：
/// 只含**真正写入**的节点 ref + 跳过原因；归属边只允许由 <see cref="Persisted"/> 派生。
/// </summary>
public sealed record RoundPersistResult(
    IReadOnlyList<PersistedNodeRef> Persisted,
    IReadOnlyList<NodePersistSkip> Skipped)
{
    /// <summary>空结果。</summary>
    public static RoundPersistResult Empty { get; } =
        new(Array.Empty<PersistedNodeRef>(), Array.Empty<NodePersistSkip>());

    /// <summary>可归属的存储键集（顺序 = 落库顺序）。</summary>
    public IReadOnlyList<string> PersistedStorageKeys
    {
        get
        {
            if (Persisted.Count == 0) return Array.Empty<string>();
            var keys = new List<string>(Persisted.Count);
            foreach (var item in Persisted) keys.Add(item.StorageKey);
            return keys;
        }
    }

    /// <summary>是否含跳过项（可见化输入）。</summary>
    public bool HasSkips => Skipped.Count > 0;
}
