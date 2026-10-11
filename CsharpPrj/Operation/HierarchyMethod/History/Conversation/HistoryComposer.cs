using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node;
using Modules.Node.Projection;
using Modules.Node.Stream;
using Operation.HierarchyMethod.History.Ports;

namespace Operation.HierarchyMethod.History.Conversation;

/// <summary>
/// 历史重组器（域无关）：读投影 → 排序（键序数 / 显式列表）→ 解码 → 消息序列 / wire。
/// <list type="bullet">
/// <item>顺序默认取**键内序数**（删掉全部边后顺序不变；排序权威 = 框架读投影）；</item>
/// <item><c>order</c> 为组装顺序**覆盖**（自由重组），未列出的键按键序数续接；</item>
/// <item><c>additions</c> 为额外键（把既有节点并回历史视图），按序附加在末尾；</item>
/// <item>不写库（重组 = 读侧投影）。</item>
/// </list>
/// </summary>
public sealed class HistoryComposer
{
    private readonly NodeFramework _framework;
    private readonly IHistoryKeyGrammar _grammar;
    private readonly IFrameSelection? _frameSelection;

    /// <param name="framework">读投影框架（域身份/位置/选择单源）。</param>
    /// <param name="grammar">键文法（帧容器反解 + 显示键合成）。</param>
    /// <param name="frameSelection">Stream 域帧级选择应用器（非 null ⇒ 单行聚合载荷展开为帧消息；null ⇒ 逐事件行直读）。</param>
    public HistoryComposer(NodeFramework framework, IHistoryKeyGrammar grammar, IFrameSelection? frameSelection = null)
    {
        _framework = framework ?? throw new ArgumentNullException(nameof(framework));
        _grammar = grammar ?? throw new ArgumentNullException(nameof(grammar));
        _frameSelection = frameSelection;
    }

    /// <summary>按读作用域重组历史消息序列。</summary>
    /// <param name="scope">读投影范围（域 / 位置 / 选择 / 键前缀 / 上限）。</param>
    /// <param name="order">显式组装顺序（键列表；空/null = 纯键序数序）。</param>
    /// <param name="additions">额外键（自由重组）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<IReadOnlyList<ComposedHistoryMessage>> ComposeAsync(
        ReadScope scope,
        IReadOnlyList<string>? order = null,
        IReadOnlyList<string>? additions = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        // Stream 域：单行聚合载荷不得被节点级选择裁剪⇒ 投影固定节点级 all；SelectionId 语义降为帧级选择。
        var projectionScope = _frameSelection is null ? scope : scope with { SelectionId = null };
        var items = await _framework.Projection.ProjectAsync(projectionScope, null, ct).ConfigureAwait(false);
        var messages = _frameSelection is null
            ? items.Select(ToMessage).ToList()
            : ExpandStreamFrames(items, scope.SelectionId);

        if (additions is { Count: > 0 })
        {
            var known = new HashSet<string>(messages.Select(m => m.Key), StringComparer.Ordinal);
            foreach (var key in additions.Where(k => !string.IsNullOrWhiteSpace(k)))
            {
                if (!known.Add(key))
                {
                    continue;
                }

                var extra = await ReadByKeyAsync(scope, key, ct).ConfigureAwait(false);
                if (extra is not null)
                {
                    messages.Add(extra);
                }
            }
        }

        return ApplyOrder(messages, order);
    }

    /// <summary>消息序列 → wire 文本（LLM 投喂形态；可逐字复现：同 selection/order ⇒ 同文本）。</summary>
    public static string ToWire(IReadOnlyList<ComposedHistoryMessage> messages)
        => messages is null || messages.Count == 0
            ? string.Empty
            : string.Join("\n", messages.Select(m => $"[{m.Role}] {m.Text}"));

    /// <summary>显式顺序覆盖（列出的键按给定顺序，其余按键序数续接）。</summary>
    private static IReadOnlyList<ComposedHistoryMessage> ApplyOrder(
        List<ComposedHistoryMessage> messages, IReadOnlyList<string>? order)
    {
        if (order is not { Count: > 0 })
        {
            return messages;
        }

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < order.Count; i++)
        {
            index.TryAdd(order[i], i);
        }

        return messages
            .OrderBy(m => index.TryGetValue(m.Key, out var position) ? position : int.MaxValue)
            .ToList();
    }

    /// <summary>按键读单节点载荷（additions 用；键不在任何可读位置 → null，不伪造）。</summary>
    private async Task<ComposedHistoryMessage?> ReadByKeyAsync(ReadScope scope, string key, CancellationToken ct)
    {
        foreach (var placement in _framework.Placements.All.Where(p => p.IsReadable))
        {
            if (scope.PlacementIds is { Count: > 0 } ids && !ids.Contains(placement.PlacementId, StringComparer.Ordinal))
            {
                continue;
            }

            if (!_framework.Carriers.TryGet(placement.CarrierId, out var carrier))
            {
                continue;
            }

            var record = await carrier.ReadAsync(key, ct).ConfigureAwait(false);
            if (record is null)
            {
                continue;
            }

            var payload = _framework.FormCodec.Decode(placement.Form, record.Json);
            return ToMessage(new NodeProjectionItem(
                key, placement.PlacementId, Flatten(payload), ProjectionOrigin.Derived, payload));
        }

        return null;
    }

    private static ComposedHistoryMessage ToMessage(NodeProjectionItem item)
    {
        var payload = ParseObject(item.PayloadJson);
        return new ComposedHistoryMessage(
            item.Key,
            Field(item, payload, "role"),
            Field(item, payload, "kind"),
            Field(item, payload, "text"),
            Field(item, payload, "mark"),
            NullIfEmpty(Field(item, payload, "parent")));
    }

    /// <summary>
    /// Stream 域展开：单行聚合载荷经 <see cref="StreamPayloadReader"/> 解析 → 帧级选择 → 帧消息序列。
    /// 选择未声明/不可识别 ⇒ 空集（⊥，不放行，不伪造）；分区键与容器不一致 ⇒ 该行不采信。
    /// </summary>
    private List<ComposedHistoryMessage> ExpandStreamFrames(
        IReadOnlyList<NodeProjectionItem> items, string? selectionId)
    {
        var messages = new List<ComposedHistoryMessage>();
        if (_frameSelection is null ||
            !_frameSelection.TryResolve(selectionId, out var predicate, out _) || predicate is null)
        {
            return messages;
        }

        foreach (var item in items)
        {
            if (!_grammar.TryParseContainer(item.Key, out var container))
            {
                continue;
            }

            if (!StreamPayloadReader.TryRead(
                    item.PayloadJson, container, StreamPayloadSchema.Default, out var payload, out _) ||
                payload is null)
            {
                continue;
            }

            foreach (var frame in payload.Frames)
            {
                if (predicate.Accept(frame.Fields))
                {
                    messages.Add(ToMessage(container, frame));
                }
            }
        }

        return messages;
    }

    /// <summary>帧记录 → 消息（显示键 = 域帧级显示键；帧字段面与原逐事件载荷逐字等价）。</summary>
    private ComposedHistoryMessage ToMessage(string container, StreamFrameRecord frame)
    {
        var fields = frame.Fields;
        var kind = fields.TryGetValue("kind", out var value) && !string.IsNullOrEmpty(value)
            ? value
            : frame.Kind;
        return new ComposedHistoryMessage(
            _grammar.DisplayKey(container, frame.Round, kind, frame.Seq),
            FieldOf(fields, "role"),
            kind,
            FieldOf(fields, "text"),
            FieldOf(fields, "mark"),
            NullIfEmpty(FieldOf(fields, "parent")));
    }

    private static string FieldOf(IReadOnlyDictionary<string, string> fields, string name)
        => fields.TryGetValue(name, out var value) ? value : string.Empty;

    /// <summary>字段取值：优先扁平字段（读投影已解码），回退载荷 JSON（按键直读路径）。</summary>
    private static string Field(NodeProjectionItem item, JsonObject? payload, string name)
    {
        if (item.Fields.TryGetValue(name, out var field) && !string.IsNullOrEmpty(field))
        {
            return field;
        }

        return payload is not null && payload.TryGetPropertyValue(name, out var node) && node is not null
            ? node.ToString()
            : string.Empty;
    }

    private static JsonObject? ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, string> Flatten(string payload)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var obj = ParseObject(payload);
        if (obj is null)
        {
            return fields;
        }

        foreach (var pair in obj)
        {
            if (pair.Value is not null)
            {
                fields[pair.Key] = ValueToText(pair.Value);
            }
        }

        return fields;
    }

    /// <summary>JSON 值 → 文本（标量原样，复合类型取 JSON 文本；与读投影的扁平口径一致）。</summary>
    private static string ValueToText(JsonNode node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node.ToJsonString();

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}
