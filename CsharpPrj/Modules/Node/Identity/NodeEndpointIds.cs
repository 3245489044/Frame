using Modules.Parsing.Keys;

namespace Modules.Node.Identity;

/// <summary>
/// 图端点 Id 约定（history / node）的**文法声明单源**（DS-P1b-2 收编；D1-② 口径）。
/// <para>
/// 事实面：<c>history.{historyId}</c>（history 容器端点）与 <c>{workflowId}.{nodeKey}</c>（业务节点端点）。
/// 后者与 KeyMapping 节点级规范键**同一键空间**（图端点 Id ≡ 该键），故只此一处声明；
/// 词法 / 反解 / 族判据一律取键文法内核 <see cref="KeyGrammar"/>（本工程禁自建第二套正则）。
/// </para>
/// <para>
/// 与 <see cref="NodeStoreKeyGrammars"/> 的分工：本类管**图端点 Id**（单族前缀 <c>history.</c> 与无族前缀节点端点），
/// **不入** <see cref="NodeStoreKeyGrammars.Registry"/> —— 该注册表是节点库键族的读侧归属入口，
/// 无族前缀的端点 Id 入册只会扩大消歧面而不增判定力（对齐「未命中 ≠ 不存在」）。
/// </para>
/// <para>
/// 剥离口径：<see cref="TryParseNodeKey"/> 以**已知 workflowId** 定位前缀（= <see cref="Node"/> 的逐字逆），
/// 而非按「首分隔切分」猜测 —— workflowId 自身含 <c>.</c> 时首分隔切分会歧义。
/// 分隔符与族前缀均取自文法内字面段，禁第二处字面量。
/// </para>
/// </summary>
public static class NodeEndpointIds
{
    /// <summary>历史容器端点 Id 文法（<c>history.{historyId}</c>）。</summary>
    public static readonly KeyGrammar HistoryGrammar =
        KeyGrammar.Create("endpoint-history", "history.{historyId}", familyPrefix: "history.");

    /// <summary>业务节点端点 Id 文法（<c>{workflowId}.{nodeKey}</c>；与 KeyMapping 节点级规范键同形同源）。</summary>
    public static readonly KeyGrammar NodeGrammar =
        KeyGrammar.Create("endpoint-node", "{workflowId}.{nodeKey}");

    /// <summary>历史端点 Id 前缀（<c>history.</c>；取自文法族前缀）。</summary>
    public static readonly string HistoryPrefix = HistoryGrammar.FamilyPrefix;

    /// <summary>节点端点 Id 段分隔符（<c>.</c>；取自文法内字面段）。</summary>
    public static readonly string Separator = NodeGrammar.LiteralBefore("nodeKey");

    /// <summary>历史容器端点 Id（<c>history.{historyId}</c>；两端空白裁剪，宽松代入与既有拼串逐字一致）。</summary>
    public static string History(string? historyId)
        => HistoryGrammar.BuildLenient((historyId ?? string.Empty).Trim());

    /// <summary>业务节点端点 Id（<c>{workflowId}.{nodeKey}</c>；宽松代入，与既有拼串逐字一致）。</summary>
    public static string Node(string? workflowId, string? nodeKey)
        => NodeGrammar.BuildLenient(workflowId, nodeKey);

    /// <summary>由历史端点 Id 还原 historyId（含 <c>.</c> 的 Id 整段归还）；非历史端点返回 false。</summary>
    public static bool TryParseHistoryId(string? resourceId, out string historyId)
    {
        historyId = string.Empty;
        if (string.IsNullOrWhiteSpace(resourceId) || !HistoryGrammar.TryParse(resourceId, out var values))
        {
            return false;
        }

        historyId = values.Text("historyId") ?? string.Empty;
        return historyId.Length > 0;
    }

    /// <summary>
    /// 由节点端点 Id 还原 nodeKey（须落在给定 workflowId 命名空间内；<paramref name="workflowId"/> 为空
    /// ⇒ 整串视为裸键，保留既有容忍口径，不静默丢弃）。
    /// </summary>
    public static bool TryParseNodeKey(string? resourceId, string? workflowId, out string nodeKey)
    {
        nodeKey = string.Empty;
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return false;
        }

        if (string.IsNullOrEmpty(workflowId))
        {
            nodeKey = resourceId;
            return true;
        }

        var prefix = workflowId + Separator;
        if (!resourceId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        nodeKey = resourceId[prefix.Length..];
        return nodeKey.Length > 0;
    }
}
