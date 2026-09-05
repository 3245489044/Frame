
using System.Text.Json.Nodes;
using Modules.Parsing.Parsing;
using Common.PlaceholderFramework.TemplateExpansion.Core;

namespace Common.PlaceholderFramework.TemplateExpansion.Adapters;

public static class FileDrivenAdapter
{
    public static JsonObject ConvertBareNamesToPrefixed(JsonObject json)
    {
        ConvertNode(json);
        return json;
    }

    private static void ConvertNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("template", out var templateNode) &&
                templateNode is JsonValue templateVal &&
                templateVal.TryGetValue<string>(out var templateName) &&
                !IsPrefixed(templateName))
            {
                obj["template"] = $"{PrefixResolver.Template}{templateName}";
            }

            if (obj.TryGetPropertyValue("style", out var styleNode) &&
                styleNode is JsonValue styleVal &&
                styleVal.TryGetValue<string>(out var styleName) &&
                !IsPrefixed(styleName))
            {
                obj["style"] = $"{PrefixResolver.Style}{styleName}";
            }

            if (obj.TryGetPropertyValue("extends", out var extendsNode) &&
                extendsNode is JsonValue extendsVal &&
                extendsVal.TryGetValue<string>(out var extendsName) &&
                !IsPrefixed(extendsName))
            {
                obj["extends"] = $"{PrefixResolver.Extends}{extendsName}";
            }

            foreach (var kv in obj)
            {
                if (kv.Value != null)
                    ConvertNode(kv.Value);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item != null)
                    ConvertNode(item);
            }
        }
    }

    private static bool IsPrefixed(string value) =>
        value.StartsWith("@");

    public static Action? MigrationCallback { get; set; }

    public static void MigrateFromFileDriven()
    {
        MigrationCallback?.Invoke();
    }
}
