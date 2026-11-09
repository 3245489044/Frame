using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Plate.JsonMethod.Serialization;
using Share.Conversation.Ids;

namespace Share.Conversation.Facts;

/// <summary>
/// 线格式 DTO（跨语言契约 <c>session-fact.v1.schema.json</c> 的 CLR 投影）：
/// <c>payload</c> 在线上是**嵌套 JSON 对象**，在内存是 <see cref="FactEnvelope.PayloadJson"/> 原文字符串。
/// </summary>
/// <param name="V">线格式版本。</param>
/// <param name="Key">归属键文本。</param>
/// <param name="Kind">事实类别。</param>
/// <param name="Seq">同文档内序号。</param>
/// <param name="AtUtc">发生时刻。</param>
/// <param name="Actor">执行体。</param>
/// <param name="Source">来源。</param>
/// <param name="Authority">权威级。</param>
/// <param name="Payload">载荷对象。</param>
/// <param name="ExternalRefs">外部代号。</param>
public sealed record FactWire(
    string V,
    string Key,
    string Kind,
    long Seq,
    DateTimeOffset AtUtc,
    string? Actor,
    FactSource Source,
    FactAuthority Authority,
    JsonElement Payload,
    IReadOnlyList<ExternalRef> ExternalRefs);

/// <summary>
/// 事实线格式编解码（**单一实现**）—— JSONL 存储、跨语言 wire、后续 SQLite 列载荷共用同一套字段名与枚举形态。
/// <para>
/// 契约纪律：新增字段 = 增 schema + 升版本（<c>V</c>），**禁改既有字段语义**；两端消费同一文件
/// （P2-13 起由 <c>[Obs]</c> 契约经 <c>Usharp.Cli</c> 发射生成）。
/// </para>
/// </summary>
public static class FactWireCodec
{
    /// <summary>序列化选项（**平台单源** = JsonContext.Compact：camelCase / 忽略 null / 枚举名 / 中文不转义）。</summary>
    public static readonly JsonSerializerOptions Options = JsonSerializerFactory.GetOptions(JsonContext.Compact);

    /// <summary>序列化为一行 JSON（不含换行符）。</summary>
    /// <param name="fact">事实条目。</param>
    public static string Serialize(FactEnvelope fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        // 载荷非法即 fail-fast：静默替换为 {} 会把「产事实错误」压平成「合法空载荷」，
        // 使下游无法发现（NoSilentOutlet 的 T 出口）。SessionFactWriter 已在写入口拒收。
        if (!FactPayload.IsWellFormed(fact.PayloadJson))
            throw new InvalidOperationException(
                $"事实载荷非法（kind={fact.Kind}）：须为 JSON 对象，实得 '{fact.PayloadJson}'");

        var root = ConversationJson.Root(fact.PayloadJson);
        var wire = new FactWire(
            fact.V,
            fact.KeyText,
            fact.Kind,
            fact.Seq,
            fact.AtUtc,
            fact.Actor,
            fact.Source,
            fact.Authority,
            root,
            fact.ExternalRefs ?? Array.Empty<ExternalRef>());

        return ConversationJson.Serialize(wire, Options);
    }

    /// <summary>解析一行 JSON（键不合法 / JSON 破损 / 载荷非对象 → false，**不猜测**）。</summary>
    /// <param name="line">一行 JSON 文本。</param>
    /// <param name="fact">out：事实条目（失败为 null）。</param>
    public static bool TryDeserialize(string? line, out FactEnvelope? fact)
        => TryDeserialize(line, out fact, out _);

    /// <summary>
    /// 解析一行 JSON，并给出失败原因（键/载荷形态可定位；行号由 JSONL 读取方补）。
    /// <para>载荷非对象 = 非法行（与 <see cref="Serialize"/> 的 fail-fast 一致）：禁静默替换为 <c>{}</c>。</para>
    /// </summary>
    /// <param name="line">一行 JSON 文本。</param>
    /// <param name="fact">out：事实条目（失败为 null）。</param>
    /// <param name="error">out：失败原因（成功为 null）。</param>
    public static bool TryDeserialize(string? line, out FactEnvelope? fact, out string? error)
    {
        fact = null;
        error = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            error = "empty-line";
            return false;
        }

        FactWire? wire;
        try
        {
            wire = ConversationJson.Deserialize<FactWire>(line!, Options);
        }
        catch (JsonException ex)
        {
            error = "invalid-json: " + ex.Message;
            return false;
        }

        if (wire is null)
        {
            error = "null-wire";
            return false;
        }

        if (!FactId.TryParse(wire.Key, out var key))
        {
            error = $"invalid-key: {wire.Key}";
            return false;
        }

        if (wire.Payload.ValueKind != JsonValueKind.Object)
        {
            // 载荷契约 = JSON 对象；非对象（数组/字符串/数字/布尔/null/缺失）一律判非法行。
            error = $"payload-not-object: key={wire.Key}, shape={wire.Payload.ValueKind}";
            return false;
        }

        fact = new FactEnvelope(
            string.IsNullOrWhiteSpace(wire.V) ? FactEnvelope.CurrentVersion : wire.V,
            key,
            wire.Kind,
            wire.Seq,
            wire.AtUtc,
            wire.Actor,
            wire.Source,
            wire.Authority,
            wire.Payload.GetRawText(),
            wire.ExternalRefs ?? Array.Empty<ExternalRef>());
        return true;
    }
}
