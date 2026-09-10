
using System.Text.Json.Nodes;
using Modules.Pipeline;
using TConfig;
// 占位符替换逻辑（${param}）已统一收敛至 Modules.Parsing.Parsing.PlaceholderResolver，
// 此处通过别名引用以避免与同名业务类冲突；本类仅保留模板展开专用的业务方法（ApplyParameters / ExpandMultiObjectItems）。
using ParsingPlaceholderResolver = Modules.Parsing.Parsing.PlaceholderResolver;

namespace Common.PlaceholderFramework.TemplateExpansion.Converters;

public static class PlaceholderResolver
{
    public static void ApplyParameters(JsonObject target, JsonObject parameters, JsonObject input, ErrorCollector? errors = null)
    {
        foreach (var param in parameters)
        {
            var paramObj = param.Value?.AsObject();
            if (paramObj == null) continue;

            bool isRequired = paramObj.TryGetPropertyValue("required", out var reqNode) &&
                              reqNode is JsonValue reqVal && reqVal.TryGetValue<bool>(out var req) && req;

            string? value = null;

            if (input.ContainsKey(param.Key))
            {
                value = input[param.Key]?.ToString();
            }
            else if (paramObj.TryGetPropertyValue("default", out var defaultNode) && defaultNode != null)
            {
                value = defaultNode.ToString();
            }
            else if (isRequired)
            {
                errors?.Add($"TEMPLATE_PARAM_{param.Key}_MISSING",
                    $"Required parameter '{param.Key}' is missing and no default value provided",
                    ErrorLevel.Error);
                continue;
            }
            else
            {
                continue;
            }

            if (value != null)
                ParsingPlaceholderResolver.ReplacePlaceholders(target, param.Key, value);
        }
    }

    public static JsonArray ExpandMultiObjectItems(JsonArray items, JsonObject input, ErrorCollector? errors = null)
    {
        var results = new JsonArray();
        foreach (var subNode in items)
        {
            var subObj = subNode?.AsObject();
            if (subObj == null) continue;

            var result = new JsonObject();
            foreach (var kv in subObj)
            {
                if (kv.Key != "defaults" && kv.Key != "parameters")
                    result[kv.Key] = kv.Value?.DeepClone();
            }

            if (subObj.ContainsKey("defaults"))
            {
                foreach (var kv in subObj["defaults"]!.AsObject())
                    result[kv.Key] = kv.Value?.DeepClone();
            }

            if (subObj.ContainsKey("parameters"))
                ApplyParameters(result, subObj["parameters"]!.AsObject(), input, errors);

            results.Add(result);
        }
        return results;
    }
}
