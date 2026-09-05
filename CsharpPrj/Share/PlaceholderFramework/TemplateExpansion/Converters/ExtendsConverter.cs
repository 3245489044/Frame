
using System.Text.Json.Nodes;
using Modules.Parsing.Parsing;
using Modules.Pipeline;
using TConfig;
using Common.PlaceholderFramework.TemplateExpansion.Abstractions;

namespace Common.PlaceholderFramework.TemplateExpansion.Converters;

public class ExtendsConverter : JsonConverterBase
{
    private readonly ITemplateStorage _storage;

    public ExtendsConverter(ITemplateStorage storage)
        : base("Extends", 50, false)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    protected override JsonObject? Convert(JsonObject json, ErrorCollector errors, PipelineContext context)
    {
        if (!json.ContainsKey("extends"))
            return null;

        var extendsNode = json["extends"];
        var extendsName = JsonPipelineHelper.GetResolvedName(json, "extends", PrefixResolver.Extends);

        if (string.IsNullOrEmpty(extendsName))
        {
            errors.Add("EXTENDS_INVALID_FORMAT",
                $"Extends field must be a string, got {extendsNode?.GetType().Name}",
                ErrorLevel.Warning);
            return null;
        }

        var templateDef = _storage.GetComposite(extendsName);
        if (templateDef == null)
        {
            errors.Add("EXTENDS_NOT_FOUND",
                $"Extends template '{extendsName}' not found in template storage",
                ErrorLevel.Warning);
            return null;
        }

        if (PushAndCheckCircular(context, "Extends", extendsName, errors))
            return null;

        try
        {
            var merged = templateDef.DeepClone().AsObject();

            JsonMerger.Merge(merged, json, new HashSet<string> { "extends" });

            merged.Remove("extends");

            json.Clear();
            foreach (var key in merged.Select(kv => kv.Key).ToList())
            {
                json[key] = merged[key]?.DeepClone();
            }

            return json;
        }
        finally
        {
            PopFromStack(context, "Extends");
        }
    }
}
