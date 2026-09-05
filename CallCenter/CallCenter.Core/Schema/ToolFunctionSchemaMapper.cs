using System.Text.Json;
using System.Text.Json.Nodes;
using CallCenter.Core.Tools;

namespace CallCenter.Core.Schema;

/// <summary>导出给模型（LLM Function Calling）的工具 schema（语言无关的中间形态，供 Agent 侧适配到本地契约）。</summary>
public sealed record ToolFunctionSchema(
    string Name,
    string Description,
    JsonElement InputSchema);

/// <summary>
/// 工具面 → 模型 Function schema 的派生规则（§15.4 GetToolsAsync 派生）：从 <see cref="ToolDescriptor.InputSchema"/>
/// （参数名 → {type, optional}）构造 OpenAI 风格 JSON Schema，并过滤 <see cref="ToolFlags.NotExposedToModel"/>
/// （未来 Agent 内部控制工具不暴露给模型，防递归）。
/// </summary>
public static class ToolFunctionSchemaMapper
{
    public static IReadOnlyList<ToolFunctionSchema> MapExposed(IEnumerable<ToolDescriptor> tools)
        => (tools ?? Array.Empty<ToolDescriptor>())
            .Where(t => !t.Flags.HasFlag(ToolFlags.NotExposedToModel))
            .Select(t => new ToolFunctionSchema(t.Name, t.Description, BuildInputSchema(t)))
            .ToArray();

    private static JsonElement BuildInputSchema(ToolDescriptor tool)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        if (tool.InputSchema != null)
        {
            foreach (var (paramName, raw) in tool.InputSchema)
            {
                Dictionary<string, object?>? meta = null;
                if (raw is Dictionary<string, object?> m)
                    meta = m;

                var isOptional = meta is not null
                                 && meta.TryGetValue("optional", out var o)
                                 && o is bool ob
                                 && ob;

                properties[paramName] = BuildProperty(meta);
                if (!isOptional) required.Add(paramName);
            }
        }

        var root = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Count > 0) root["required"] = required;

        return JsonSerializer.SerializeToElement(root);
    }

    private static JsonObject BuildProperty(Dictionary<string, object?>? meta)
    {
        var jsonType = meta is not null && meta.TryGetValue("type", out var t) && t is string ts
            ? NormalizeJsonType(ts)
            : "object";
        var prop = new JsonObject { ["type"] = jsonType };
        if (meta is not null && meta.TryGetValue("description", out var desc) && desc is string dstr
            && !string.IsNullOrWhiteSpace(dstr))
            prop["description"] = dstr;
        return prop;
    }

    private static string NormalizeJsonType(string typeName)
    {
        var t = typeName.Trim();
        switch (t)
        {
            case "System.String": return "string";
            case "System.Boolean": return "boolean";
            case "System.Int16":
            case "System.Int32":
            case "System.Int64":
            case "System.UInt16":
            case "System.UInt32":
            case "System.UInt64":
            case "System.Single":
            case "System.Double":
            case "System.Decimal": return "number";
            default:
                return t.ToLowerInvariant() switch
                {
                    "string" => "string",
                    "boolean" or "bool" => "boolean",
                    "integer" => "integer",
                    "number" or "float" or "double" => "number",
                    "array" => "array",
                    "object" => "object",
                    "null" => "null",
                    _ => "object",
                };
        }
    }
}
