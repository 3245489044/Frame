
using System.Text.Json.Nodes;
using Modules.Parsing.Parsing;
using Modules.Pipeline;
using TConfig;
using Common.PlaceholderFramework.TemplateExpansion.Abstractions;

namespace Common.PlaceholderFramework.TemplateExpansion.Converters;

public class StyleApplyConverter : JsonConverterBase
{
    // 样式可应用的结构化子节点键：控件嵌套位 + steps/commands/views 数组位。
    // 与 TraverseByKey 配合实现"按指定键递归遍历"，避免误入 parameters/events 等非结构化字段。
    private static readonly string[] StyleTraversalKeys =
        JsonPipelineHelper.ChildControlKeys.Concat(new[] { "steps", "commands", "views" }).ToArray();

    private readonly ITemplateStorage _storage;

    public StyleApplyConverter(ITemplateStorage storage)
        : base("StyleApply", 250, false)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    protected override JsonObject? Convert(JsonObject json, ErrorCollector errors, PipelineContext context)
    {
        bool modified = JsonPipelineHelper.TraverseByKey(
            json,
            StyleTraversalKeys,
            node => node.ContainsKey("style") && ApplyStyle(node, errors));

        return modified ? json : null;
    }

    private bool ApplyStyle(JsonObject target, ErrorCollector errors)
    {
        if (!target.ContainsKey("style"))
            return false;

        var styleNode = target["style"];

        string? styleName = null;
        switch (styleNode)
        {
            case JsonObject:
                errors.Add("STYLE_INLINE_OBJECT",
                    "Inline style object is not supported; use a style reference string (e.g. @style.myStyle)",
                    ErrorLevel.Warning);
                return false;

            case JsonValue:
                styleName = JsonPipelineHelper.GetResolvedName(target, "style", PrefixResolver.Style);
                break;

            default:
                errors.Add("STYLE_INVALID_FORMAT",
                    $"Style field must be a string or object, got {styleNode?.GetType().Name}",
                    ErrorLevel.Warning);
                return false;
        }

        var style = _storage.GetStyle(styleName);
        if (style == null)
        {
            errors.Add("STYLE_NOT_FOUND",
                $"Style '{styleName}' not found in template storage",
                ErrorLevel.Warning);
            return false;
        }

        if (style.ContainsKey("applyTo"))
        {
            var applyTo = style["applyTo"]!.AsArray();
            var targetType = target.ContainsKey("type")
                ? target["type"]?.GetValue<string>()
                : null;

            if (targetType != null &&
                !applyTo.Contains("*") &&
                !applyTo.Any(a => a?.GetValue<string>() == targetType))
            {
                return false;
            }
        }

        if (style.ContainsKey("properties"))
        {
            var properties = style["properties"]!.AsObject();
            foreach (var kv in properties)
            {
                if (!target.ContainsKey(kv.Key))
                {
                    target[kv.Key] = kv.Value?.DeepClone();
                }
            }
        }

        target.Remove("style");

        // 注意：原 ApplyStyle 内对 ChildControlKeys 的二次递归已上移至 Convert 的 TraverseByKey，
        // 单个 ApplyStyle 调用只负责当前节点的样式应用，递归遍历由泛型 helper 统一处理。

        return true;
    }
}
