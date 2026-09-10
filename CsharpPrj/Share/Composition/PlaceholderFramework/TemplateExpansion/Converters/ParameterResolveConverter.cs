
using System.Text.Json.Nodes;
using Modules.Parsing.Parsing;
using Modules.Pipeline;
using TConfig;
using Common.PlaceholderFramework.TemplateExpansion.Abstractions;

namespace Common.PlaceholderFramework.TemplateExpansion.Converters;

public class ParameterResolveConverter : JsonConverterBase
{
    public ParameterResolveConverter()
        : base("ParameterResolve", 400, false)
    {
    }

    protected override JsonObject? Convert(JsonObject json, ErrorCollector errors, PipelineContext context)
    {
        bool modified = false;

        if (!json.ContainsKey("parameters"))
            return null;

        var parameters = json["parameters"]!.AsObject();
        var resolvedParams = new Dictionary<string, JsonNode?>();

        foreach (var kv in parameters)
        {
            var resolved = ResolveParameter(kv.Key, kv.Value, json, errors);
            resolvedParams[kv.Key] = resolved;
        }

        modified |= ReplaceInputReferences(json, resolvedParams, errors);

        modified |= json.Remove("parameters");

        return modified ? json : null;
    }

    private JsonNode? ResolveParameter(string name, JsonNode? definition, JsonObject root, ErrorCollector errors)
    {
        if (definition is JsonObject defObj)
        {
            if (defObj.ContainsKey("default"))
                return defObj["default"]?.DeepClone();

            if (defObj.ContainsKey("env"))
            {
                var envName = defObj["env"]!.GetValue<string>();
                var envValue = Environment.GetEnvironmentVariable(envName);
                if (envValue != null)
                    return JsonValue.Create(envValue);
            }
        }

        return definition?.DeepClone();
    }

    private bool ReplaceInputReferences(JsonNode node, Dictionary<string, JsonNode?> parameters, ErrorCollector errors)
    {
        bool modified = false;

        switch (node)
        {
            case JsonObject obj:
                foreach (var kv in obj.ToList())
                {
                    if (kv.Value is JsonValue val && val.TryGetValue<string>(out var str))
                    {
                        // 复用 PrefixResolver 的 @input 正则与替换实现，避免本地副本与 Parsing 模块漂移。
                        var refs = PrefixResolver.ExtractInputReferences(str);
                        if (refs.Count == 0)
                            continue;

                        if (refs.Count == 1 && refs[0] == str)
                        {
                            // 整串即 @input.X：替换为参数节点本身（保留 JsonNode 类型，避免标量化）。
                            var paramName = PrefixResolver.ResolveName(refs[0], PrefixResolver.Input);
                            if (parameters.TryGetValue(paramName, out var paramValue))
                            {
                                obj[kv.Key] = paramValue?.DeepClone();
                                modified = true;
                            }
                            else
                            {
                                errors.Add($"PARAM_{paramName}_NOT_FOUND",
                                    $"Parameter '{paramName}' not found (referenced as @input.{paramName})",
                                    ErrorLevel.Warning);
                            }
                        }
                        else
                        {
                            // 部分匹配：用字符串形式批量替换（参数值需可字符串化）。
                            var stringParams = BuildStringParams(parameters);
                            var replaced = PrefixResolver.ReplaceAllInputReferences(str, stringParams);
                            if (replaced != str)
                            {
                                obj[kv.Key] = JsonValue.Create(replaced);
                                modified = true;
                            }
                        }
                    }
                    else if (kv.Value is JsonObject || kv.Value is JsonArray)
                    {
                        if (ReplaceInputReferences(kv.Value!, parameters, errors))
                            modified = true;
                    }
                }
                break;

            case JsonArray arr:
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonObject || arr[i] is JsonArray)
                    {
                        if (ReplaceInputReferences(arr[i]!, parameters, errors))
                            modified = true;
                    }
                }
                break;
        }

        return modified;
    }

    private static Dictionary<string, string> BuildStringParams(Dictionary<string, JsonNode?> parameters)
    {
        var dict = new Dictionary<string, string>(parameters.Count);
        foreach (var p in parameters)
        {
            if (p.Value is JsonValue jv && jv.TryGetValue<string>(out var s))
                dict[p.Key] = s;
        }
        return dict;
    }
}
