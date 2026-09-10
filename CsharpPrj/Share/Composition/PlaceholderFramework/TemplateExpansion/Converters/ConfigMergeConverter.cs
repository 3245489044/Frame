
using System.Text.Json.Nodes;
using Modules.Pipeline;
using TConfig;
using Common.PlaceholderFramework.TemplateExpansion.Abstractions;

namespace Common.PlaceholderFramework.TemplateExpansion.Converters;

public class ConfigMergeConverter : JsonConverterBase
{
    public ConfigMergeConverter()
        : base("ConfigMerge", 500, false)
    {
    }

    protected override JsonObject? Convert(JsonObject json, ErrorCollector errors, PipelineContext context)
    {
        bool modified = false;

        if (json.ContainsKey("baseConfig") && json["baseConfig"] is JsonObject baseConfig)
        {
            modified |= JsonMerger.Merge(json, baseConfig);
        }

        if (json.ContainsKey("overrideConfig") && json["overrideConfig"] is JsonObject overrideConfig)
        {
            modified |= JsonMerger.Merge(json, overrideConfig);
        }

        if (json.ContainsKey("views"))
        {
            var views = json["views"]!.AsArray();
            foreach (var viewNode in views)
            {
                var view = viewNode?.AsObject();
                if (view == null) continue;

                if (view.ContainsKey("config") && view["config"] is JsonObject viewConfig)
                {
                    modified |= JsonMerger.Merge(view, viewConfig);
                }
            }
        }

        return modified ? json : null;
    }
}
