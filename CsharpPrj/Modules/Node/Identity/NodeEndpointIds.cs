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

    /// <summary>
    /// 节点存储键文法（<c>{nodeKey}[@{instanceKey}]</c>；实例后缀可缺席 = legacy 裸键）。
    /// 实例分隔符 <c>@</c> 由本模板声明一次，禁第二处字面量。
    /// </summary>
    public static readonly KeyGrammar NodeStorageGrammar =
        KeyGrammar.Create("node-address", "{nodeKey}[@{instanceKey}]");

    /// <summary>带实例的节点端点 Id 文法（<c>{workflowId}.{nodeKey}[@{instanceKey}]</c>）。</summary>
    public static readonly KeyGrammar InstanceEndpointGrammar =
        KeyGrammar.Create("endpoint-node-instance", "{workflowId}.{nodeKey}[@{instanceKey}]");

    /// <summary>实例分隔符（<c>@</c>；取自存储键文法内字面段）。</summary>
    public static readonly string InstanceSeparator = NodeStorageGrammar.LiteralBefore("instanceKey");

    /// <summary>历史端点 Id 前缀（<c>history.</c>；取自文法族前缀）。</summary>
    public static readonly string HistoryPrefix = HistoryGrammar.FamilyPrefix;

    /// <summary>历史端点 ResourceType 前缀（<c>history:</c>；由族前缀派生，禁第二处字面量）。</summary>
    private static readonly string HistoryTypePrefix = HistoryPrefix.TrimEnd('.') + ":";

    /// <summary>节点端点 Id 段分隔符（<c>.</c>；取自文法内字面段）。</summary>
    public static readonly string Separator = NodeGrammar.LiteralBefore("nodeKey");

    /// <summary>
    /// 历史身份归一（端点 Id 幂等单点）：调用方传入 <c>242e</c> / <c>history:242e</c> / <c>history.242e</c> /
    /// 历史三重前缀 <c>history.history:242e</c> 一律产出同一裸身份；<see cref="History"/> 与迁移/门禁共用本实现。
    /// </summary>
    public static string NormalizeHistoryId(string? historyId)
        => NormalizeBare(historyId, HistoryTypePrefix, HistoryPrefix);

    /// <summary>历史容器端点 Id（<c>history.{historyId}</c>；幂等：身份内已带的 <c>history:</c>/<c>history.</c> 先剥净）。</summary>
    public static string History(string? historyId)
        => HistoryGrammar.BuildLenient(NormalizeHistoryId(historyId));

    /// <summary>业务节点端点 Id（<c>{workflowId}.{nodeKey}</c>；幂等：<c>nodeKey</c> 已带本 workflowId 前缀时先剥一次）。</summary>
    public static string Node(string? workflowId, string? nodeKey)
    {
        var wf = (workflowId ?? string.Empty).Trim();
        var key = (nodeKey ?? string.Empty).Trim();
        if (wf.Length > 0)
        {
            key = StripOnce(key, wf + Separator);
        }
        return NodeGrammar.BuildLenient(wf, key);
    }

    /// <summary>剥掉身份内层叠的 <c>{type}:</c> / 族前缀（有界收敛，保证构造是不动点）。</summary>
    private static string NormalizeBare(string? value, string typePrefix, string familyPrefix)
    {
        var raw = (value ?? string.Empty).Trim();
        for (var i = 0; i < 4; i++)
        {
            var next = StripOnce(StripOnce(raw, typePrefix), familyPrefix);
            if (string.Equals(next, raw, StringComparison.Ordinal)) break;
            raw = next;
        }
        return raw;
    }

    /// <summary>剥一层前缀（等值或剥空则原样返回，防「只剩前缀」被吞成空身份）。</summary>
    private static string StripOnce(string value, string prefix)
        => !string.IsNullOrEmpty(prefix)
           && value.Length > prefix.Length
           && value.StartsWith(prefix, StringComparison.Ordinal)
            ? value[prefix.Length..]
            : value;

    /// <summary>
    /// 由历史端点 Id 还原 historyId（含 <c>.</c> 的 Id 整段归还）；非历史端点返回 false。
    /// <para>返回**裸身份**（端点内的层叠前缀先归一，K1）；与端点构造互为逆（构造侧再补族前缀）。</para>
    /// </summary>
    public static bool TryParseHistoryId(string? resourceId, out string historyId)
    {
        historyId = string.Empty;
        if (string.IsNullOrWhiteSpace(resourceId) || !HistoryGrammar.TryParse(resourceId, out var values))
        {
            return false;
        }

        historyId = NormalizeBare(values.Text("historyId"), HistoryTypePrefix, HistoryPrefix);
        return historyId.Length > 0;
    }

    /// <summary>节点存储键（<c>{nodeKey}[@{instanceKey}]</c>；实例空 ⇒ 裸键）。</summary>
    public static string Storage(string? nodeKey, string? instanceKey)
        => NodeStorageGrammar.BuildLenient(nodeKey, instanceKey);

    /// <summary>带实例的节点端点 Id（<c>{workflowId}.{nodeKey}[@{instanceKey}]</c>）。</summary>
    public static string Endpoint(string? workflowId, string? nodeKey, string? instanceKey)
        => InstanceEndpointGrammar.BuildLenient(workflowId, nodeKey, instanceKey);

    /// <summary>反解存储键（实例缺席 ⇒ 空串，不返回 null 冒充「存在且为空」）。</summary>
    public static bool TryParseStorage(string? storageKey, out string nodeKey, out string instanceKey)
    {
        nodeKey = string.Empty;
        instanceKey = string.Empty;
        if (!NodeStorageGrammar.TryParse(storageKey, out var values))
        {
            return false;
        }

        nodeKey = values.Text("nodeKey") ?? string.Empty;
        instanceKey = values.Text("instanceKey") ?? string.Empty;
        return nodeKey.Length > 0;
    }

    /// <summary>反解带实例的端点 Id（三段全出；失败 ⇒ false，不猜）。</summary>
    public static bool TryParseEndpoint(
        string? endpointId, out string workflowId, out string nodeKey, out string instanceKey)
    {
        workflowId = string.Empty;
        nodeKey = string.Empty;
        instanceKey = string.Empty;
        if (!InstanceEndpointGrammar.TryParse(endpointId, out var values))
        {
            return false;
        }

        workflowId = values.Text("workflowId") ?? string.Empty;
        nodeKey = values.Text("nodeKey") ?? string.Empty;
        instanceKey = values.Text("instanceKey") ?? string.Empty;
        return nodeKey.Length > 0;
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
