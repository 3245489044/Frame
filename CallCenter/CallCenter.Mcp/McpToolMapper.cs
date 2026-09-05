using System.Text.Json;
using CallCenter.Core.Tools;

namespace CallCenter.Mcp;

/// <summary>MCP 工具 → CSG 数据面工具描述符（Configuration 投影）的纯变换器。不依赖网络/进程，可单测。</summary>
public static class McpToolMapper
{
    /// <summary>
    /// 把 MCP 端点的工具元数据投影为 <see cref="ToolDescriptor"/>：Id=tool:{backend}:{name}、backend 由分类器/调用方给定，
    /// 描述取自 MCP 说明。ExternalMCP 类工具标 <see cref="ToolFlags.Externalized"/>（挂门控），TS/PS 本地确定性工具标
    /// <see cref="ToolFlags.Deterministic"/>。InputSchema 按 <see cref="ToolDescriptor.InputSchema"/> 规范形状
    ///（参数名 → {type, optional[, description]}）折叠，供目录描述与 <see cref="CallCenter.Core.Schema.ToolFunctionSchemaMapper"/> 派生
    /// Function schema（X-0：不再把整份 JSON-Schema 顶层键当参数名塞入，避免暴露给模型失真）。
    /// </summary>
    public static ToolDescriptor ToDescriptor(McpToolInfo tool, string backend, int version = 1)
    {
        var externalized = string.Equals(backend, ToolBackend.ExternalMCP, StringComparison.OrdinalIgnoreCase);
        return new ToolDescriptor(
            Id: $"tool:{backend}:{tool.Name}",
            Name: tool.Name,
            Backend: backend,
            Version: version,
            Description: string.IsNullOrWhiteSpace(tool.Description) ? tool.Name : tool.Description!,
            InputSchema: CollapseInputSchema(tool.InputSchema),
            OutputSchema: new Dictionary<string, object?>(),
            Flags: externalized ? ToolFlags.Externalized : ToolFlags.Deterministic);
    }

    /// <summary>
    /// 把 MCP 工具入参 JSON-Schema 折叠为规范形状：遍历 <c>properties</c>，每参存 <c>{type: &lt;JSON 类型&gt;, optional: !required,
    /// description?}</c>。非对象 / 无 properties → 空字典（无参工具）。required 缺失视为全可选。
    /// </summary>
    private static IReadOnlyDictionary<string, object?> CollapseInputSchema(JsonElement? schema)
    {
        var map = new Dictionary<string, object?>();
        if (schema is not { } el || el.ValueKind != JsonValueKind.Object)
            return map;

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (el.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in req.EnumerateArray())
                if (r.ValueKind == JsonValueKind.String) required.Add(r.GetString()!);
        }

        if (!el.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
            return map;

        foreach (var p in props.EnumerateObject())
        {
            var meta = new Dictionary<string, object?>
            {
                ["type"] = ReadJsonType(p.Value),
                ["optional"] = !required.Contains(p.Name),
            };
            if (p.Value.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(d.GetString()))
                meta["description"] = d.GetString();
            map[p.Name] = meta;
        }

        return map;
    }

    /// <summary>读属性声明的 JSON 类型关键字；缺省（如仅 enum / 引用类型）回落 object。</summary>
    private static object ReadJsonType(JsonElement property)
    {
        if (property.ValueKind == JsonValueKind.Object
            && property.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
            return t.GetString()!;
        return "object";
    }
}
