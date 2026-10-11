using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Materialization;
using Modules.Node.Stream;
using Operation.HierarchyMethod.History.Content;
using Operation.HierarchyMethod.History.Ports;

namespace Operation.HierarchyMethod.History.Conversation;

/// <summary>追加结果（mark 语义 + 物化结果）。</summary>
/// <param name="Mark">归一的标记（未识别 → 原值）。</param>
/// <param name="Materialize">物化结果。</param>
public sealed record HistoryAppendResult(string Mark, NodeContentMaterializeOutcome Materialize);

/// <summary>
/// 历史追加服务（域无关）：把**人工/外部**节点物化为历史节点（经唯一写门），并表达 <c>mark</c> 语义。
/// <list type="bullet">
/// <item>不覆盖既有键：追加键由调用方给出，同键重复即幂等 upsert（同 Digest → <c>Unchanged</c>）；</item>
/// <item><c>mark</c> 写进**载荷**（读侧 <c>selection</c> 是裁剪唯一入口）——不再依赖节点状态，
/// 使「哪些进上下文」完全由读侧声明决定；</item>
/// <item>归属边不在本层写：由上层按结果语义编排。</item>
/// </list>
/// </summary>
public sealed class HistoryAppendService
{
    private readonly NodeContentMaterializeService _writer;
    private readonly IHistoryKeyGrammar _grammar;
    private readonly HistoryMarkVocabulary _marks;
    private readonly IStreamRegistry _streamRegistry;

    /// <param name="writer">消息域内容物化服务（经唯一写门）。</param>
    /// <param name="grammar">键文法（身份键构造 / 身份值袋）。</param>
    /// <param name="marks">入史标记词表（归一 + 标记→类别映射）。</param>
    /// <param name="streamRegistry">消息域 Stream 运行时（聚合累积器门面）。</param>
    public HistoryAppendService(
        NodeContentMaterializeService writer,
        IHistoryKeyGrammar grammar,
        HistoryMarkVocabulary marks,
        IStreamRegistry streamRegistry)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _grammar = grammar ?? throw new ArgumentNullException(nameof(grammar));
        _marks = marks ?? throw new ArgumentNullException(nameof(marks));
        _streamRegistry = streamRegistry ?? throw new ArgumentNullException(nameof(streamRegistry));
    }

    /// <summary>追加一条历史节点（载荷须为 JSON 对象；mark 注入载荷后经唯一写门物化）。</summary>
    /// <param name="key">追加键（须与消息域身份模板同形）。</param>
    /// <param name="json">节点载荷 JSON（对象）。</param>
    /// <param name="mark">具名标记（空 = None）。</param>
    /// <param name="ct">取消令牌。</param>
    public Task<HistoryAppendResult> AppendAsync(
        string key, string json, string? mark, CancellationToken ct = default)
        => AppendStreamAsync(key, 1, 0, json, mark, ct);

    /// <summary>按容器/轮次/到达序追加（键自动构造；内容类别经标记词表单源映射）。</summary>
    /// <param name="container">容器（会话名 / 任务键）。</param>
    /// <param name="round">轮次号。</param>
    /// <param name="ordinal">轮内全局到达序。</param>
    /// <param name="json">节点载荷 JSON（对象）。</param>
    /// <param name="mark">具名标记（空 = None）。</param>
    /// <param name="ct">取消令牌。</param>
    public Task<HistoryAppendResult> AppendAsync(
        string container, int round, long ordinal, string json, string? mark, CancellationToken ct = default)
        => AppendStreamAsync(container, round, ordinal, json, mark, ct);

    /// <summary>追加核心（mark 归一 + 载荷校验 = 单源；写侧经单节点化消息域容器）。</summary>
    private async Task<HistoryAppendResult> AppendStreamAsync(
        string container, int round, long ordinal, string json, string? mark, CancellationToken ct)
    {
        var normalized = _marks.Normalize(mark);
        if (normalized is null)
        {
            return new HistoryAppendResult(
                mark ?? string.Empty,
                Reject(container, $"mark.unrecognized（允许：{string.Join("/", _marks.Marks)}）"));
        }

        if (!TryEmbedMark(json, normalized, out var payload, out var reason))
        {
            return new HistoryAppendResult(normalized, Reject(container, reason));
        }

        var kind = _marks.KindOfMark(normalized);
        var normalizedRound = round > 0 ? round : 1;
        var standalone = !_streamRegistry.TryGet(container, out var run) || run is null || run.Round < 0;
        run = standalone ? _streamRegistry.Begin(container, normalizedRound) : run!;

        var frame = run.Append(kind, FrameFields(payload, ordinal), AtUtcOf(payload));
        if (frame is null)
        {
            return new HistoryAppendResult(
                normalized,
                Reject(container, "append.rejected（未成轮 / 同 kind 连续同指纹去重 / 超上限）"));
        }

        if (standalone)
        {
            var aggregate = await run.SnapshotAggregateAsync().ConfigureAwait(false);
            if (aggregate is not null)
            {
                // 跨轮累积：与既有单行载荷按轮合并（命令面多次追加不丢历史轮）。
                var mergedPayload = await StreamPersistedMerge
                    .MergeWithExistingAsync(_writer.Framework, aggregate.PositionKey, aggregate.PayloadJson, ct)
                    .ConfigureAwait(false);
                var materialized = await _writer
                    .MaterializeAsync(
                        aggregate.IdentityKey, mergedPayload, _grammar.ValuesOfContainer(container), ct)
                    .ConfigureAwait(false);
                _streamRegistry.Release(container);
                return new HistoryAppendResult(normalized, materialized);
            }
        }

        return new HistoryAppendResult(
            normalized,
            new NodeContentMaterializeOutcome(
                container, MaterializeStatus.Written, 0, 0, 0, 0, 0, Array.Empty<string>()));
    }

    private static NodeContentMaterializeOutcome Reject(string key, string reason)
        => new(key, MaterializeStatus.Rejected, 0, 0, 0, 1, 0, new[] { reason });

    /// <summary>帧字段袋（载荷对象逐字段 + 到达序；<c>kind/seq/atUtc</c> 由累积器注入，此处不含）。</summary>
    private static IReadOnlyDictionary<string, object?> FrameFields(string payload, long ordinal)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal) { ["ordinal"] = ordinal };
        if (TryParseObject(payload, out var obj))
        {
            foreach (var pair in obj)
            {
                fields[pair.Key] = pair.Value switch
                {
                    null => null,
                    JsonValue value when value.TryGetValue<string>(out var text) => text,
                    _ => pair.Value.ToJsonString(),
                };
            }
        }

        return fields;
    }

    /// <summary>帧到达时刻（载荷 <c>at_unix_ms &gt; 0</c> 用之，否则取当前 UTC）。</summary>
    private static DateTimeOffset AtUtcOf(string payload)
    {
        if (TryParseObject(payload, out var obj) &&
            long.TryParse(obj["at_unix_ms"]?.ToString(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var ms) && ms > 0)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }

        return DateTimeOffset.UtcNow;
    }

    /// <summary>把 mark 注入载荷对象（非对象载荷 → 拒绝，不静默包裹）。</summary>
    private static bool TryEmbedMark(string json, string mark, out string payload, out string reason)
    {
        payload = json ?? string.Empty;
        reason = string.Empty;

        if (!TryParseObject(json, out var obj))
        {
            reason = "payload.not-json-object（追加载荷须为 JSON 对象）";
            return false;
        }

        obj["mark"] = mark;
        payload = obj.ToJsonString(HistoryPayloadJson.Options);
        return true;
    }

    /// <summary>载荷解析（对象才可注入 mark；非对象/非法 → false，不静默包裹）。</summary>
    private static bool TryParseObject(string? json, out JsonObject obj)
    {
        obj = new JsonObject();
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonObject parsed)
            {
                return false;
            }

            obj = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
