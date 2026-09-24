using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Share.Conversation.Facts;

/// <summary>
/// 事实载荷读写助手（**载荷键名单源**）——<c>payload</c> 为 kind 专属的浅层 JSON 对象，键名一律小写下划线
/// （与 <c>[Obs]</c> 声明的 <c>required=</c>/<c>metrics=</c> 字段同一命名面）。
/// <para>
/// 纪律：产出侧一律经 <see cref="Compose"/>（禁各产事实点自拼 JSON 键名）；消费侧一律经
/// <c>Read*</c>（非法 / 缺失一律返回缺省，**不抛出** —— 事实层容错、判据层可见化）。
/// </para>
/// </summary>
public static class FactPayload
{
    /// <summary>空载荷（无字段事实的规范表达，避免 null 冒充）。</summary>
    public const string Empty = "{}";

    /// <summary>
    /// 组装浅层 JSON 对象（**只收非 null 字段**：缺字段即「无此事实」，不写 null 冒充 ——
    /// 对齐 <c>[Obs]</c> 的 required 零容忍语义）。
    /// </summary>
    /// <param name="fields">字段序列（键名小写下划线）。</param>
    public static string Compose(params (string Key, object? Value)[] fields)
    {
        if (fields is null || fields.Length == 0) return Empty;

        var payload = new Dictionary<string, object?>(fields.Length, StringComparer.Ordinal);
        foreach (var (key, value) in fields)
        {
            if (string.IsNullOrWhiteSpace(key) || value is null) continue;
            payload[key] = value;
        }

        return payload.Count == 0 ? Empty : JsonSerializer.Serialize(payload);
    }

    /// <summary>是否为合法 JSON 对象载荷（非对象 / 非法 JSON 一律 false；写入侧据此拒收）。</summary>
    /// <param name="payloadJson">载荷文本。</param>
    public static bool IsWellFormed(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return false;
        try
        {
            using var document = JsonDocument.Parse(payloadJson!);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>读字符串字段（按候选键序首命中；缺失 / 空值 → null）。</summary>
    /// <param name="payloadJson">载荷文本。</param>
    /// <param name="keys">候选键名（含历史写法兼容位）。</param>
    public static string? ReadString(string? payloadJson, params string[] keys)
        => Find(payloadJson, keys, element => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null,
        });

    /// <summary>读布尔字段（缺失 / 类型不符 → <paramref name="fallback"/>）。</summary>
    /// <param name="payloadJson">载荷文本。</param>
    /// <param name="fallback">缺失时的缺省值。</param>
    /// <param name="keys">候选键名。</param>
    public static bool ReadBool(string? payloadJson, bool fallback, params string[] keys)
    {
        var found = false;
        var value = false;
        ForEachCandidate(payloadJson, keys, element =>
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.True:
                    found = true; value = true; break;
                case JsonValueKind.False:
                    found = true; value = false; break;
                case JsonValueKind.String when bool.TryParse(element.GetString(), out var parsed):
                    found = true; value = parsed; break;
            }
            return found;
        });
        return found ? value : fallback;
    }

    /// <summary>读整数字段（缺失 / 非法 → null）。</summary>
    /// <param name="payloadJson">载荷文本。</param>
    /// <param name="keys">候选键名。</param>
    public static long? ReadLong(string? payloadJson, params string[] keys)
        => Find(payloadJson, keys, element => element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt64(out var value) ? value : (long?)null,
            JsonValueKind.String => long.TryParse(element.GetString(), out var parsed) ? parsed : (long?)null,
            _ => null,
        });

    /// <summary>读浮点字段（缺失 / 非法 → null）。</summary>
    /// <param name="payloadJson">载荷文本。</param>
    /// <param name="keys">候选键名。</param>
    public static double? ReadDouble(string? payloadJson, params string[] keys)
        => Find(payloadJson, keys, element => element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDouble(out var value) ? value : (double?)null,
            JsonValueKind.String => double.TryParse(element.GetString(), out var parsed) ? parsed : (double?)null,
            _ => null,
        });

    /// <summary>载荷规模（字符数；事实只存摘要 + <c>text_len</c> 的体积判据）。</summary>
    /// <param name="payloadJson">载荷文本。</param>
    public static int LengthOf(string? payloadJson) => payloadJson?.Length ?? 0;

    /// <summary>载荷的 UTF-8 字节数（体积 / 成本判据用）。</summary>
    /// <param name="payloadJson">载荷文本。</param>
    public static int Utf8ByteCount(string? payloadJson)
        => string.IsNullOrEmpty(payloadJson) ? 0 : Encoding.UTF8.GetByteCount(payloadJson!);

    /// <summary>诊断预览（截断显示，只供日志可见化；**不用于判据**）。</summary>
    /// <param name="payloadJson">载荷文本。</param>
    /// <param name="maxChars">最大字符数。</param>
    public static string Preview(string? payloadJson, int maxChars = 200)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return string.Empty;
        var text = payloadJson!.Replace('\r', ' ').Replace('\n', ' ');
        return text.Length <= maxChars ? text : text[..maxChars] + "…";
    }

    /// <summary>按候选键序投影首命中值（引用类型与可空值类型通用）。</summary>
    private static T? Find<T>(string? payloadJson, string[] keys, Func<JsonElement, T?> project)
    {
        T? result = default;
        ForEachCandidate(payloadJson, keys, element =>
        {
            var value = project(element);
            if (value is null) return false;
            result = value;
            return true;
        });
        return result;
    }

    /// <summary>遍历候选键（首个命中即停；非法 JSON 静默返回，交由上层可见化）。</summary>
    private static void ForEachCandidate(string? payloadJson, string[] keys, Func<JsonElement, bool> onHit)
    {
        if (string.IsNullOrWhiteSpace(payloadJson) || keys is null || keys.Length == 0) return;

        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(payloadJson!);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return;

            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (!document.RootElement.TryGetProperty(key, out var element)) continue;
                if (onHit(element)) return;
            }
        }
        catch (JsonException)
        {
            // 载荷非法：事实层容错（不抛出），由判据层经 Completeness / Projector 可见化。
        }
        finally
        {
            document?.Dispose();
        }
    }
}
