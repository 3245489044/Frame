namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 三层层级（history / task / node）引用图资源 Id 约定的**唯一权威** —— 端点 Id 的构造与还原集中于此，
/// 消除各写 / 读入口各自拼接（如 <c>"history."</c> 前缀、<c>{workflowId}.</c> 前缀）导致的分叉。
/// <para>约定：history → <c>history.{historyId}</c>；task → taskKey（无前缀）；node → <c>{workflowId}.{nodeKey}</c>。</para>
/// </summary>
public static class HierarchyResourceKeys
{
    /// <summary>历史端点 Id 前缀。</summary>
    public const string HistoryPrefix = "history.";

    /// <summary>历史端点 Id（<c>history.{historyId}</c>；两端空白裁剪）。</summary>
    public static string History(string historyId) => HistoryPrefix + (historyId ?? string.Empty).Trim();

    /// <summary>节点端点 Id（转发 <see cref="NodeRelationFactory.NodeId"/>，同源不复制）。</summary>
    public static string Node(string workflowId, string nodeKey) => NodeRelationFactory.NodeId(workflowId, nodeKey);

    /// <summary>由历史端点 Id 还原 historyId；非历史端点返回 false。</summary>
    public static bool TryParseHistoryId(string? resourceId, out string historyId)
    {
        historyId = string.Empty;
        if (string.IsNullOrWhiteSpace(resourceId) ||
            !resourceId.StartsWith(HistoryPrefix, StringComparison.Ordinal))
            return false;
        historyId = resourceId.Substring(HistoryPrefix.Length);
        return historyId.Length > 0;
    }

    /// <summary>由节点端点 Id 还原 nodeKey（须带本工作流前缀）；非本工作流端点返回 false。</summary>
    public static bool TryParseNodeKey(string? resourceId, string? workflowId, out string nodeKey)
    {
        nodeKey = string.Empty;
        if (string.IsNullOrWhiteSpace(resourceId)) return false;
        var prefix = string.IsNullOrEmpty(workflowId) ? string.Empty : workflowId + ".";
        if (prefix.Length > 0 && !resourceId.StartsWith(prefix, StringComparison.Ordinal)) return false;
        nodeKey = resourceId.Substring(prefix.Length);
        return nodeKey.Length > 0;
    }
}
