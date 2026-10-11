using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Operation.HierarchyMethod.History.Content;

/// <summary>
/// 归档**单行**载荷的编解码**单源**：
/// <c>{ "rounds": [ { "round": n, "messages": "原文", "status": "..." } ] }</c>。
/// <para>
/// 语义：数组序 = 轮序；<c>messages</c> 为**不透明原文**（整轮原文全文，逐字可还原，禁二次解析）；
/// 同轮 upsert（覆盖）。读写两侧同调此处，禁各自拼 JSON（漂移即读面对拍失效）。
/// </para>
/// <para>域无关：轮内容语义由宿主承载（如 Cline messages.json 全文）；本类只做 <c>rounds[]</c> 容器编解码。</para>
/// </summary>
public static class RoundArchivePayload
{
    /// <summary>轮数组字段名。</summary>
    public const string RoundsField = "rounds";

    /// <summary>轮次字段名。</summary>
    public const string RoundField = "round";

    /// <summary>该轮原文（不透明）字段名。</summary>
    public const string MessagesField = "messages";

    /// <summary>归档状态字段名（completed / faulted / partial）。</summary>
    public const string StatusField = "status";

    /// <summary>一轮归档条目（<paramref name="Messages"/> = 该轮原文，逐字）。</summary>
    /// <param name="Round">轮次（从 1 起）。</param>
    /// <param name="Messages">该轮原文全文（不透明）。</param>
    /// <param name="Status">归档状态（可空）。</param>
    public readonly record struct Entry(int Round, string Messages, string? Status);

    /// <summary>解析载荷为轮条目集（空 / 非法 JSON / 无 <c>rounds</c> ⇒ 空集，不抛；读取路径不因脏载荷中断）。</summary>
    /// <param name="payload">归档单行载荷。</param>
    public static IReadOnlyList<Entry> Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<Entry>();
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            return Array.Empty<Entry>();
        }

        if (node is not JsonObject obj || obj[RoundsField] is not JsonArray rounds)
        {
            return Array.Empty<Entry>();
        }

        var list = new List<Entry>(rounds.Count);
        foreach (var item in rounds)
        {
            if (item is not JsonObject entry)
            {
                continue;
            }

            var round = ReadRound(entry[RoundField]);
            if (round <= 0)
            {
                continue;
            }

            list.Add(new Entry(round, ReadString(entry[MessagesField]) ?? string.Empty, ReadString(entry[StatusField])));
        }

        return list;
    }

    /// <summary>合并一轮（同轮覆盖）并序列化（唯一序列化点；轮序升序）。</summary>
    /// <param name="payload">已有载荷（可空 = 新历史）。</param>
    /// <param name="round">本轮轮次。</param>
    /// <param name="messages">本轮原文（不透明）。</param>
    /// <param name="status">归档状态（可空）。</param>
    public static string Merge(string? payload, int round, string? messages, string? status)
    {
        var byRound = new Dictionary<int, Entry>();
        foreach (var entry in Parse(payload))
        {
            byRound[entry.Round] = entry;
        }

        byRound[round] = new Entry(round, messages ?? string.Empty, status);

        var rounds = new JsonArray();
        foreach (var entry in byRound.Values.OrderBy(e => e.Round))
        {
            var item = new JsonObject
            {
                [RoundField] = entry.Round,
                [MessagesField] = entry.Messages,
            };
            if (!string.IsNullOrEmpty(entry.Status))
            {
                item[StatusField] = entry.Status;
            }

            rounds.Add(item);
        }

        var root = new JsonObject { [RoundsField] = rounds };
        return root.ToJsonString();
    }

    private static int ReadRound(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return 0;
        }

        if (value.TryGetValue<int>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) &&
               int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static string? ReadString(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
