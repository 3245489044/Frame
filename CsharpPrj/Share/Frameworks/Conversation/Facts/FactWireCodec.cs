using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    /// <summary>序列化选项（camelCase 字段名 / 枚举名原样 / 中文不转义以便人可读取证）。</summary>
    public static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>序列化为一行 JSON（不含换行符）。</summary>
    /// <param name="fact">事实条目。</param>
    public static string Serialize(FactEnvelope fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        // 载荷在读侧即成对校验（SessionFactWriter 拒收非法载荷）；此处为防御性兜底：非法即空对象。
        var payloadJson = FactPayload.IsWellFormed(fact.PayloadJson)
            ? fact.PayloadJson
            : FactPayload.Empty;

        using var payload = JsonDocument.Parse(payloadJson);
        var wire = new FactWire(
            fact.V,
            fact.KeyText,
            fact.Kind,
            fact.Seq,
            fact.AtUtc,
            fact.Actor,
            fact.Source,
            fact.Authority,
            payload.RootElement.Clone(),
            fact.ExternalRefs ?? Array.Empty<ExternalRef>());

        return JsonSerializer.Serialize(wire, Options);
    }

    /// <summary>解析一行 JSON（键不合法 / JSON 破损 → false，**不猜测**）。</summary>
    /// <param name="line">一行 JSON 文本。</param>
    /// <param name="fact">out：事实条目（失败为 null）。</param>
    public static bool TryDeserialize(string? line, out FactEnvelope? fact)
    {
        fact = null;
        if (string.IsNullOrWhiteSpace(line)) return false;

        FactWire? wire;
        try
        {
            wire = JsonSerializer.Deserialize<FactWire>(line!, Options);
        }
        catch (JsonException)
        {
            return false;
        }

        if (wire is null) return false;
        if (!FactId.TryParse(wire.Key, out var key)) return false;

        fact = new FactEnvelope(
            string.IsNullOrWhiteSpace(wire.V) ? FactEnvelope.CurrentVersion : wire.V,
            key,
            wire.Kind,
            wire.Seq,
            wire.AtUtc,
            wire.Actor,
            wire.Source,
            wire.Authority,
            wire.Payload.ValueKind == JsonValueKind.Object ? wire.Payload.GetRawText() : FactPayload.Empty,
            wire.ExternalRefs ?? Array.Empty<ExternalRef>());
        return true;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
