using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CallCenter.LlmGate.Fingerprint;

/// <summary>
/// 请求指纹：规范化 JSON（键排序、剔除易变字段）后 SHA256。
/// 同 body ⇒ 同 hash；键序不同 ⇒ 同 hash；非 JSON 回退 trimmed raw。
/// </summary>
public static class RequestFingerprint
{
    /// <summary>剔除的顶层易变字段（循环检测主键必须稳定）。</summary>
    private static readonly HashSet<string> VolatileTopLevelKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "created",
        "request_id",
    };

    /// <summary>剔除的嵌套易变字段（metadata.timestamp）。</summary>
    private const string MetadataKey = "metadata";
    private const string MetadataTimestampKey = "timestamp";

    public static string Compute(string? body)
    {
        var canonical = Normalize(body);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>规范化文本（供测试与排查；指纹只取其 SHA256）。</summary>
    public static string Normalize(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            var node = JsonNode.Parse(body);
            if (node is null)
            {
                return body.Trim();
            }

            var sb = new StringBuilder(body.Length);
            WriteNode(node, sb);
            return sb.ToString();
        }
        catch (JsonException)
        {
            // 非 JSON body：稳定化为 trimmed raw（仍满足同 body 同 hash）。
            return body.Trim();
        }
    }

    private static void WriteNode(JsonNode? node, StringBuilder sb)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                return;
            case JsonObject obj:
                WriteObject(obj, sb);
                return;
            case JsonArray array:
                sb.Append('[');
                for (var i = 0; i < array.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    WriteNode(array[i], sb);
                }

                sb.Append(']');
                return;
            case JsonValue value:
                sb.Append(value.ToJsonString());
                return;
            default:
                sb.Append(node.ToJsonString());
                return;
        }
    }

    private static void WriteObject(JsonObject obj, StringBuilder sb)
    {
        sb.Append('{');
        var keys = obj.Select(p => p.Key)
            .Where(k => !VolatileTopLevelKeys.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        var first = true;
        foreach (var key in keys)
        {
            var value = obj[key];
            if (string.Equals(key, MetadataKey, StringComparison.OrdinalIgnoreCase) && value is JsonObject meta)
            {
                value = StripMetadataTimestamp(meta);
            }

            if (!first)
            {
                sb.Append(',');
            }

            first = false;
            sb.Append(JsonSerializer.Serialize(key));
            sb.Append(':');
            WriteNode(value, sb);
        }

        sb.Append('}');
    }

    private static JsonObject StripMetadataTimestamp(JsonObject meta)
    {
        if (!meta.Any(p => string.Equals(p.Key, MetadataTimestampKey, StringComparison.OrdinalIgnoreCase)))
        {
            return meta;
        }

        var clone = new JsonObject();
        foreach (var pair in meta)
        {
            if (string.Equals(pair.Key, MetadataTimestampKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            clone[pair.Key] = pair.Value?.DeepClone();
        }

        return clone;
    }
}
