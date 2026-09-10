
using System.Text.Json.Nodes;
using Modules.Pipeline;
using TConfig;
using Common.PlaceholderFramework.TemplateExpansion.Abstractions;

namespace Common.PlaceholderFramework.TemplateExpansion.Converters;

public class DefaultsApplyConverter : JsonConverterBase
{
    private readonly ITemplateStorage _storage;

    public DefaultsApplyConverter(ITemplateStorage storage)
        : base("DefaultsApply", 300, false)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    protected override JsonObject? Convert(JsonObject json, ErrorCollector errors, PipelineContext context)
    {
        bool modified = false;

        var globalDefaults = _storage.GetComposite("_defaults");
        if (globalDefaults != null)
        {
            modified |= ApplyDefaults(json, globalDefaults);
        }

        // 类型默认值仅在控件嵌套位（ChildControlKeys）下递归应用，不进入 steps/commands/views，
        // 保持原语义：避免误把 _<type>_defaults 应用到动作/视图项的同名字段。
        modified |= JsonPipelineHelper.TraverseByKey(
            json,
            JsonPipelineHelper.ChildControlKeys,
            ApplyTypeDefaults);

        return modified ? json : null;
    }

    private bool ApplyTypeDefaults(JsonObject obj)
    {
        if (!obj.ContainsKey("type") || obj["type"] is not JsonValue typeValue)
            return false;

        var typeName = typeValue.GetValue<string>();
        var defaultsKey = $"_{typeName}_defaults";
        var typeDefaults = _storage.GetComposite(defaultsKey);
        if (typeDefaults == null)
            return false;

        return ApplyDefaults(obj, typeDefaults);
    }

    private static bool ApplyDefaults(JsonObject target, JsonObject defaults)
    {
        bool modified = false;
        foreach (var kv in defaults)
        {
            if (!target.ContainsKey(kv.Key))
            {
                target[kv.Key] = kv.Value?.DeepClone();
                modified = true;
            }
        }
        return modified;
    }
}
