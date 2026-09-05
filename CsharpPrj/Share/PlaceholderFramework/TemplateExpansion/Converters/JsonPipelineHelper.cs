
using System.Text.Json.Nodes;
using Modules.Parsing.Parsing;

namespace Common.PlaceholderFramework.TemplateExpansion.Converters;

public static class JsonPipelineHelper
{
    public static readonly string[] ChildControlKeys = { "controls", "panel1Controls", "panel2Controls" };

    public static bool TraverseTopLevel(
        JsonObject json,
        Func<JsonArray, bool>? processControl = null,
        Func<JsonArray, bool>? processItem = null)
    {
        bool modified = false;

        if (processControl != null && json.ContainsKey("views"))
        {
            var views = json["views"]!.AsArray();
            foreach (var viewNode in views)
            {
                var view = viewNode?.AsObject();
                if (view == null) continue;

                if (view.ContainsKey("controls"))
                {
                    modified |= processControl(view["controls"]!.AsArray());
                }
            }
        }

        if (processItem != null && json.ContainsKey("steps"))
        {
            modified |= processItem(json["steps"]!.AsArray());
        }

        if (processItem != null && json.ContainsKey("commands"))
        {
            modified |= processItem(json["commands"]!.AsArray());
        }

        return modified;
    }

    public static bool TraverseControls(
        JsonArray controls,
        Func<JsonObject, bool> processControl)
    {
        bool modified = false;
        foreach (var controlNode in controls)
        {
            var control = controlNode?.AsObject();
            if (control == null) continue;

            if (processControl(control))
                modified = true;

            foreach (var key in ChildControlKeys)
            {
                if (control.ContainsKey(key) && control[key] is JsonArray childArray)
                {
                    if (TraverseControls(childArray, processControl))
                        modified = true;
                }
            }
        }
        return modified;
    }

    public static bool TraverseAll(JsonNode node, Func<JsonObject, bool> processNode)
    {
        bool modified = false;

        switch (node)
        {
            case JsonObject obj:
                if (processNode(obj))
                    modified = true;

                foreach (var kv in obj)
                {
                    if (kv.Value != null)
                    {
                        if (TraverseAll(kv.Value, processNode))
                            modified = true;
                    }
                }
                break;

            case JsonArray arr:
                foreach (var item in arr)
                {
                    if (item != null)
                    {
                        if (TraverseAll(item, processNode))
                            modified = true;
                    }
                }
                break;
        }

        return modified;
    }

    public static string? GetStringValue(JsonObject obj, string key)
    {
        if (obj.TryGetPropertyValue(key, out var node) &&
            node is JsonValue val &&
            val.TryGetValue<string>(out var str))
        {
            return str;
        }
        return null;
    }

    /// <summary>
    /// 提取指定键的字符串值并解析前缀（如 @template.、@style.、@extends.），返回去掉前缀的名称。
    /// 统一替代各 Converter 中重复的 TryGetPropertyValue + JsonValue + ResolveName 模式。
    /// </summary>
    public static string? GetResolvedName(JsonObject obj, string key, string prefix)
    {
        var str = GetStringValue(obj, key);
        return str == null ? null : PrefixResolver.ResolveName(str, prefix);
    }

    public static bool HasKeyOfType(JsonObject obj, string key, System.Text.Json.JsonValueKind kind)
    {
        return obj.TryGetPropertyValue(key, out var node) &&
               node != null &&
               node.GetValueKind() == kind;
    }

    /// <summary>
    /// 按 <paramref name="childKeys"/> 递归遍历 JsonObject 中的数组成员，对每个 JsonObject 调用 <paramref name="processNode"/>。
    /// 与 <see cref="TraverseAll"/> 不同：仅进入指定 childKeys（如 controls/steps/commands/views），
    /// 避免误入 parameters/events 等非结构化字段。返回是否有任何节点被修改。
    /// </summary>
    public static bool TraverseByKey(
        JsonObject obj,
        string[] childKeys,
        Func<JsonObject, bool> processNode)
    {
        bool modified = processNode(obj);

        foreach (var key in childKeys)
        {
            if (obj.TryGetPropertyValue(key, out var node) && node is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (item is JsonObject childObj)
                        modified |= TraverseByKey(childObj, childKeys, processNode);
                }
            }
        }

        return modified;
    }
}
