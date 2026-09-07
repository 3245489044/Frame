using System.Text.Json;

namespace CallCenter.Mcp;

/// <summary>
/// MCP 传输线缆的通用协议原语（SingleImplementation / TH-12）——统一承载 "MCP 端点 ↔ 上层值" 的往返变换，
/// 供 <see cref="McpClientConnection"/>（stdio）与 <see cref="CallCenterHttpMcpClient"/>（Streamable HTTP）等所有
/// client 共用，不再各 client 自写第二份入参解析 / content 提取（对齐 FormatFidelity / Collect 确定性提取语义）。
/// 与具体端点 / 传输方式无关，是纯静态可单测函数。
/// </summary>
public static class McpProtocol
{
    /// <summary>把 JSON 对象入参解析为 SDK 可空值字典；空 / 非法入参 → null（无实参调用）。</summary>
    public static IReadOnlyDictionary<string, object?>? ParseArguments(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return null;

        var obj = JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson);
        if (obj is null)
            return null;

        var map = new Dictionary<string, object?>(obj.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in obj)
            map[kv.Key] = kv.Value;
        return map;
    }

    /// <summary>
    /// 把 MCP CallTool 响应的 content（多为 text 片段数组）拼接为文本；无可提取片段时回退整段序列化 JSON。
    /// 这是从统一 JSON 线缆到结果的确定性提取，不绑定具体 content 子类型。
    /// </summary>
    public static string ExtractText(object content)
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(content);
        }
        catch
        {
            return string.Empty;
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return json;

        var parts = new List<string>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String)
            {
                var s = text.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    parts.Add(s);
            }
        }

        return parts.Count > 0 ? string.Join(Environment.NewLine, parts) : json;
    }
}
