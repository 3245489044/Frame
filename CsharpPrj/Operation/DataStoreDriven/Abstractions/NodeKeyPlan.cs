using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 节点键归一**单源**（DUP-3）：裸键 / <c>{wf}.{node}</c> / <c>nodes.{wf}.{node}</c> ⇒ 裸键；
/// 实例键（<c>{node}@{task}</c>）原样保留（实例后缀是身份组成部分，禁截断）。
/// <para>装配层（HierarchyMethod）与关系层（DataStoreDriven）共用本实现，禁各处再写键规则。</para>
/// </summary>
public static class NodeKeyPlan
{
    /// <summary>默认工作流命名空间（唯一权威；<c>NodeKeys.DefaultWorkflowId</c> 引用本常量）。</summary>
    public const string DefaultNamespace = "agent-round";

    /// <summary>命名空间归一（空 / null ⇒ <see cref="DefaultNamespace"/>）。</summary>
    public static string NormalizeNamespace(string? workflowId)
        => string.IsNullOrEmpty(workflowId) ? DefaultNamespace : workflowId;

    /// <summary>实例键分隔符（唯一权威；<c>NodeInstanceRef.InstanceSeparator</c> 引用本常量）。</summary>
    public const char InstanceSeparator = '@';

    /// <summary>是否实例存储键形态（<c>{node}@{instance}</c>）。</summary>
    public static bool IsInstanceKey(string? storageKey)
        => !string.IsNullOrEmpty(storageKey) && storageKey.IndexOf(InstanceSeparator) > 0;

    /// <summary>归一为裸 nodeKey（空白 ⇒ 空串；实例键原样返回）。</summary>
    public static string NodeKey(string? nodeKey)
    {
        if (string.IsNullOrWhiteSpace(nodeKey)) return string.Empty;
        var key = nodeKey.Trim();

        if (IsInstanceKey(key)) return key;

        const string nodesPrefix = "nodes.";
        if (key.StartsWith(nodesPrefix, StringComparison.Ordinal))
            key = key.Substring(nodesPrefix.Length);

        var dot = key.IndexOf('.');
        return dot > 0 ? key.Substring(dot + 1) : key;
    }

    /// <summary>批量归一 + 去重（保序；空白丢弃）。</summary>
    public static IReadOnlyList<string> Normalize(IReadOnlyCollection<string>? nodeKeys)
        => (nodeKeys ?? Array.Empty<string>())
            .Select(NodeKey)
            .Where(static key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
