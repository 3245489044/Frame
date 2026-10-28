using System;
using System.Collections.Generic;
using Modules.KeySpace.Model;
using Modules.KeySpace.Rules;
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
    private static readonly KeyFamily HistoryFamily = HierarchyKeyFamilies.History;
    private static readonly KeyFamily NodeFamily = HierarchyKeyFamilies.Node;
    private static readonly KeyFamily SessionFamily = HierarchyKeyFamilies.Session;

    private static readonly KeyOps HierarchyKeys = KeyOps.FromFamilies(HierarchyKeyFamilies.All);

    /// <summary>键面投影（框架默认声明；行键/端点形态只经此产生，禁第二处拼接）。</summary>
    private static readonly KeyProjection Projection = new(HierarchyKeyFamilies.All);

    /// <summary>历史容器端点 Id 文法（<c>history.{historyId}</c>）。</summary>
    public static readonly KeyGrammar HistoryGrammar =
        KeySurfaceRules.Grammar(HistoryFamily, KeySurface.Endpoint);

    /// <summary>业务节点端点 Id 文法（<c>{workflowId}.{nodeKey}</c>；与 KeyMapping 节点级规范键同形同源）。</summary>
    public static readonly KeyGrammar NodeGrammar =
        KeySurfaceRules.Grammar(NodeFamily, KeySurface.Endpoint, 1);

    /// <summary>节点存储键文法（<c>{nodeKey}[@{instanceKey}]</c>；实例后缀可缺席 = legacy 裸键）。</summary>
    public static readonly KeyGrammar NodeStorageGrammar =
        KeySurfaceRules.Grammar(NodeFamily, KeySurface.Storage);

    /// <summary>带实例的节点端点 Id 文法（<c>{workflowId}.{nodeKey}[@{instanceKey}]</c>）。</summary>
    public static readonly KeyGrammar InstanceEndpointGrammar =
        KeySurfaceRules.Grammar(NodeFamily, KeySurface.Endpoint);

    /// <summary>会话根端点文法（<c>session.{sessionName}</c>）。</summary>
    public static readonly KeyGrammar SessionGrammar =
        KeySurfaceRules.Grammar(SessionFamily, KeySurface.Endpoint);

    /// <summary>实例分隔符（<c>@</c>；取自存储键文法内字面段）。</summary>
    public static readonly string InstanceSeparator = NodeStorageGrammar.LiteralBefore("instanceKey");

    /// <summary>历史端点 Id 前缀（<c>history.</c>；取自文法族前缀）。</summary>
    public static readonly string HistoryPrefix = HistoryGrammar.FamilyPrefix;

    /// <summary>历史端点 ResourceType 前缀（<c>history:</c>；由族前缀派生，禁第二处字面量）。</summary>
    private static readonly string HistoryTypePrefix = HistoryPrefix.TrimEnd('.') + ":";

    /// <summary>会话端点 ResourceType 前缀（<c>session:</c>；由族前缀派生）。</summary>
    private static readonly string SessionTypePrefix = SessionGrammar.FamilyPrefix.TrimEnd('.') + ":";

    /// <summary>节点端点 Id 段分隔符（<c>.</c>；取自文法内字面段）。</summary>
    public static readonly string Separator = NodeGrammar.LiteralBefore("nodeKey");

    /// <summary>
    /// 历史身份归一（端点 Id 幂等单点）：调用方传入 <c>242e</c> / <c>history:242e</c> / <c>history.242e</c> /
    /// 历史三重前缀 <c>history.history:242e</c> 一律产出同一裸身份；<see cref="History"/> 与迁移/门禁共用本实现。
    /// </summary>
    public static string NormalizeHistoryId(string? historyId)
        => NormalizeBare(historyId, HistoryTypePrefix, HistoryPrefix);

    /// <summary>历史容器端点 Id（<c>history.{historyId}</c>；空身份即抛，不产出 <c>history.</c> 僵尸键）。</summary>
    public static string History(string? historyId)
    {
        var identity = NormalizeHistoryId(historyId);
        if (identity.Length == 0)
        {
            throw new ArgumentException(
                "历史身份为空（history: / history. 不是身份）。", nameof(historyId));
        }

        return HierarchyKeys.Render(
            HierarchyKeys.With(
                HierarchyKeys.Root(HistoryFamily.Id, KeySurface.Endpoint),
                KeyAxis.Of("historyId"), identity));
    }

    /// <summary>历史身份的行键形态（<c>history:{identity}</c>；空身份 ⇒ 空串）。</summary>
    public static string ToHistoryRowKey(string? value)
    {
        var identity = NormalizeHistoryId(value);
        return identity.Length == 0
            ? string.Empty
            : Projection.Project(HistoryFamily.Id, identity, KeySurface.Storage).Value;
    }

    /// <summary>
    /// 历史身份的全部可读行键形态（规范 <c>history:{id}</c> 在前，只读兼容裸 <c>{id}</c> 随后）。
    /// <para>唯一候选键来源：读取/删除/存在性探针共用，禁再各写候选表。</para>
    /// </summary>
    public static IReadOnlyList<string> HistoryRowKeys(string? value)
    {
        var identity = NormalizeHistoryId(value);
        return identity.Length == 0
            ? Array.Empty<string>()
            : Projection.Candidates(HistoryFamily.Id, identity, KeySurface.Storage);
    }

    /// <summary>会话根身份归一（<c>session:</c> / <c>session.</c> 幂等剥净）。</summary>
    public static string NormalizeSessionId(string? sessionName)
        => NormalizeBare(sessionName, SessionTypePrefix, SessionGrammar.FamilyPrefix);

    /// <summary>会话根端点 Id（<c>session.{name}</c>；空身份即抛）。</summary>
    public static string Session(string? sessionName)
    {
        var identity = NormalizeSessionId(sessionName);
        if (identity.Length == 0)
        {
            throw new ArgumentException(
                "会话身份为空（session: / session. 不是身份）。", nameof(sessionName));
        }

        return HierarchyKeys.Render(
            HierarchyKeys.With(
                HierarchyKeys.Root(SessionFamily.Id, KeySurface.Endpoint),
                KeyAxis.Of("sessionName"), identity));
    }

    /// <summary>业务节点端点 Id（<c>{workflowId}.{nodeKey}</c>；幂等：<c>nodeKey</c> 已带本 workflowId 前缀时先剥一次）。
    /// <para>节点身份为空 ⇒ 抛（不产出 <c>.key</c> 半成品）；workflowId 为空 ⇒ 退化为存储面裸键（既有「空命名空间 = 裸键」口径）。</para></summary>
    public static string Node(string? workflowId, string? nodeKey)
    {
        var wf = (workflowId ?? string.Empty).Trim();
        var key = RequireNodeKey(nodeKey, nameof(nodeKey));
        if (wf.Length == 0)
        {
            return Storage(key, null);
        }

        key = StripOnce(key, wf + Separator);
        return HierarchyKeys.Render(
            HierarchyKeys.With(
                HierarchyKeys.With(
                    HierarchyKeys.Root(NodeFamily.Id, KeySurface.Endpoint),
                    KeyAxis.Of("workflowId"), wf),
                KeyAxis.Of("nodeKey"), key));
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

        if (raw.Length == 0
            || string.Equals(raw, typePrefix, StringComparison.Ordinal)
            || string.Equals(raw, familyPrefix, StringComparison.Ordinal))
        {
            return string.Empty;
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

    /// <summary>节点身份非空校验（空/空白 ⇒ 抛，禁产出 <c>.key</c> / <c>@inst</c> 半成品）。</summary>
    private static string RequireNodeKey(string? nodeKey, string paramName)
    {
        var key = (nodeKey ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            throw new ArgumentException("节点身份为空（nodeKey 不能为空）。", paramName);
        }

        return key;
    }

    /// <summary>已知的其它族（<c>history.*</c> / <c>session.*</c> / <c>history:</c> / <c>session:</c>）判据（结构事实，禁手列前缀）。</summary>
    public static bool IsForeignFamily(string value)
        => !string.IsNullOrWhiteSpace(value)
           && (HierarchyKeys.IsKeyOf(HistoryFamily.Id, KeySurface.Endpoint, value)
               || HierarchyKeys.IsKeyOf(HistoryFamily.Id, KeySurface.Storage, value)
               || HierarchyKeys.IsKeyOf(SessionFamily.Id, KeySurface.Endpoint, value)
               || HierarchyKeys.IsKeyOf(SessionFamily.Id, KeySurface.Storage, value));

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

    /// <summary>节点存储键（<c>{nodeKey}[@{instanceKey}]</c>；实例空 ⇒ 裸键；节点身份空 ⇒ 抛）。</summary>
    public static string Storage(string? nodeKey, string? instanceKey)
        => HierarchyKeys.Render(
            HierarchyKeys.With(
                HierarchyKeys.With(
                    HierarchyKeys.Root(NodeFamily.Id, KeySurface.Storage),
                    KeyAxis.Of("nodeKey"), RequireNodeKey(nodeKey, nameof(nodeKey))),
                KeyAxis.Of("instanceKey"), (instanceKey ?? string.Empty).Trim()));

    /// <summary>带实例的节点端点 Id（<c>{workflowId}.{nodeKey}[@{instanceKey}]</c>；节点身份空 ⇒ 抛）。</summary>
    public static string Endpoint(string? workflowId, string? nodeKey, string? instanceKey)
    {
        var wf = (workflowId ?? string.Empty).Trim();
        var key = RequireNodeKey(nodeKey, nameof(nodeKey));
        if (wf.Length == 0)
        {
            return Storage(key, instanceKey);
        }

        return HierarchyKeys.Render(
            HierarchyKeys.With(
                HierarchyKeys.With(
                    HierarchyKeys.With(
                        HierarchyKeys.Root(NodeFamily.Id, KeySurface.Endpoint),
                        KeyAxis.Of("workflowId"), wf),
                    KeyAxis.Of("nodeKey"), key),
                KeyAxis.Of("instanceKey"), (instanceKey ?? string.Empty).Trim()));
    }

    /// <summary>反解存储键（实例缺席 ⇒ 空串，不返回 null 冒充「存在且为空」）。</summary>
    public static bool TryParseStorage(string? storageKey, out string nodeKey, out string instanceKey)
    {
        nodeKey = string.Empty;
        instanceKey = string.Empty;
        if (!HierarchyKeys.TryParse(
                NodeFamily.Id, KeySurface.Storage, storageKey, out var parsed, out _))
        {
            return false;
        }

        nodeKey = parsed.Point.Text("nodeKey");
        instanceKey = parsed.Point.Text("instanceKey");
        return nodeKey.Length > 0;
    }

    /// <summary>反解带实例的端点 Id（三段全出；失败 ⇒ false，不猜）。</summary>
    public static bool TryParseEndpoint(
        string? endpointId, out string workflowId, out string nodeKey, out string instanceKey)
    {
        workflowId = string.Empty;
        nodeKey = string.Empty;
        instanceKey = string.Empty;
        if (!HierarchyKeys.TryParse(
                NodeFamily.Id, KeySurface.Endpoint, endpointId, out var parsed, out _))
        {
            return false;
        }

        workflowId = parsed.Point.Text("workflowId");
        nodeKey = parsed.Point.Text("nodeKey");
        instanceKey = parsed.Point.Text("instanceKey");
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

        var known = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["workflowId"] = workflowId,
        };

        if (!HierarchyKeys.TryParseScoped(
                NodeFamily.Id, KeySurface.Endpoint, resourceId, known, out var parsed, out _, out _))
        {
            return false;
        }

        nodeKey = parsed.Point.Text("nodeKey");
        return nodeKey.Length > 0;
    }

    /// <summary>
    /// <c>history:{identity}</c> 形态的身份反解（任务端点旧表示/迁移用）；前缀取自历史族，禁第二处字面量。
    /// </summary>
    public static bool TryParseHistoryTypeKey(string? value, out string historyId)
    {
        historyId = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith(HistoryTypePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        historyId = NormalizeHistoryId(value);
        return historyId.Length > 0;
    }

    /// <summary>
    /// 由端点/存储键/裸键还原节点身份（nodeKey）：带实例端点按面文法反解，裸键原样返回；
    /// 外来命名空间 ⇒ false（不按首个分隔符猜测，避免含点的 nodeKey 被截断）。
    /// </summary>
    public static bool TryParseNodeIdentity(
        string? resourceId, string? workflowId, out string nodeKey, string? familyHint = null)
    {
        nodeKey = string.Empty;
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return false;
        }

        // 外来族（history./session./history:/session:）不得落入节点身份兜底；node:/task: 等含类型前缀的裸键同样拒绝。
        var raw = resourceId.Trim();
        if (IsForeignFamily(raw))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(familyHint) &&
            !string.Equals(familyHint, NodeFamily.Id, StringComparison.Ordinal))
        {
            return false;
        }

        var wf = (workflowId ?? string.Empty).Trim();
        if (TryParseEndpoint(resourceId, out var parsedWf, out var parsedNode, out _) &&
            (wf.Length == 0 || string.Equals(parsedWf, wf, StringComparison.Ordinal)))
        {
            nodeKey = parsedNode;
            return nodeKey.Length > 0;
        }

        if (wf.Length > 0 && TryParseNodeKey(resourceId, wf, out nodeKey))
        {
            return nodeKey.Length > 0;
        }

        // 裸键（无命名空间前缀、不含实例分隔符、不含类型前缀）：整串视为身份，不截断。
        if (!raw.Contains(':') &&
            (wf.Length == 0 || (!raw.Contains(Separator, StringComparison.Ordinal) &&
                                !raw.Contains(InstanceSeparator, StringComparison.Ordinal))))
        {
            nodeKey = raw;
            return true;
        }

        return false;
    }
}
