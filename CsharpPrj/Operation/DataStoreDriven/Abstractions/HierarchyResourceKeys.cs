using Modules.Node.Identity;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 三层层级（history / task / node）引用图资源 Id 约定的**兼容别名门面** ——
/// 端点 Id 的构造与还原单源已上移层 2（<see cref="NodeEndpointIds"/>），本类**不含任何端点 Id 字面量**：
/// <c>history.{historyId}</c> 与 <c>{workflowId}.{nodeKey}</c> 一律转发；<c>task</c> 端点 = 裸 taskKey（无前缀，不经键文法）。
/// <para>保留门面 = 消费方零改动（别名转发一版；方案 §6-DS-P1b 条款）。</para>
/// </summary>
public static class HierarchyResourceKeys
{
    /// <summary>历史端点 Id 前缀（转发单源，禁第二处字面量）。</summary>
    public static string HistoryPrefix => NodeEndpointIds.HistoryPrefix;

    /// <summary>历史端点 Id（<c>history.{historyId}</c>；两端空白裁剪）。</summary>
    public static string History(string historyId) => NodeEndpointIds.History(historyId);

    /// <summary>节点端点 Id（<c>{workflowId}.{nodeKey}</c>；转发单源，同源不复制）。</summary>
    public static string Node(string workflowId, string nodeKey) => NodeEndpointIds.Node(workflowId, nodeKey);

    /// <summary>由历史端点 Id 还原 historyId；非历史端点返回 false。</summary>
    public static bool TryParseHistoryId(string? resourceId, out string historyId)
        => NodeEndpointIds.TryParseHistoryId(resourceId, out historyId);

    /// <summary>由节点端点 Id 还原 nodeKey（须带本工作流前缀）；非本工作流端点返回 false。</summary>
    public static bool TryParseNodeKey(string? resourceId, string? workflowId, out string nodeKey)
        => NodeEndpointIds.TryParseNodeKey(resourceId, workflowId, out nodeKey);
}
