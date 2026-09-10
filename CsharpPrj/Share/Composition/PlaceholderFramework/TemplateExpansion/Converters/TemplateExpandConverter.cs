
using System.Text.Json.Nodes;
using Modules.Parsing.Parsing;
using Modules.Pipeline;
using TConfig;
using Common.PlaceholderFramework.TemplateExpansion.Abstractions;

namespace Common.PlaceholderFramework.TemplateExpansion.Converters;

public class TemplateExpandConverter : JsonConverterBase
{
    private readonly ITemplateStorage _storage;

    public TemplateExpandConverter(ITemplateStorage storage)
        : base("TemplateExpand", 200, false)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    protected override JsonObject? Convert(JsonObject json, ErrorCollector errors, PipelineContext context)
    {
        bool modified = false;

        modified |= JsonPipelineHelper.TraverseTopLevel(
            json,
            processControl: controls => ProcessControls(controls, errors),
            processItem: items => ProcessItems(items, errors));

        return modified ? json : null;
    }

    private bool ProcessControls(JsonArray controls, ErrorCollector errors)
    {
        bool modified = false;
        for (int i = 0; i < controls.Count; i++)
        {
            var control = controls[i]?.AsObject();
            if (control == null) continue;

            if (control.ContainsKey("template"))
            {
                var expanded = ExpandTemplate(control, errors);
                if (expanded != null)
                {
                    controls[i] = expanded;
                    control = expanded;
                    modified = true;
                }
            }

            // buildEvents 落地：顶层 functionName/parameters/output → events.bindings（模板按钮与裸 functionName 按钮通用）。
            if (BuildEventsFromTemplate(control))
                modified = true;

            foreach (var key in JsonPipelineHelper.ChildControlKeys)
            {
                if (control.ContainsKey(key) && control[key] is JsonArray childArray)
                {
                    if (ProcessControls(childArray, errors))
                        modified = true;
                }
            }
        }
        return modified;
    }

    private bool ProcessItems(JsonArray items, ErrorCollector errors)
    {
        bool modified = false;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i]?.AsObject();
            if (item == null) continue;

            if (item.ContainsKey("template"))
            {
                var expanded = ExpandTemplate(item, errors);
                if (expanded != null)
                {
                    items[i] = expanded;
                    modified = true;
                }
            }
        }
        return modified;
    }

    private JsonObject? ExpandTemplate(JsonObject source, ErrorCollector errors)
    {
        var templateNode = source["template"];
        var templateName = JsonPipelineHelper.GetResolvedName(source, "template", PrefixResolver.Template);

        if (templateName == null)
        {
            errors.Add("TEMPLATE_INVALID_FORMAT",
                $"Template field must be a string, got {templateNode?.GetType().Name}",
                ErrorLevel.Warning);
            return null;
        }

        var composite = _storage.GetComposite(templateName);
        if (composite == null)
        {
            errors.Add("TEMPLATE_NOT_FOUND",
                $"Template '{templateName}' not found in template storage",
                ErrorLevel.Warning);
            return null;
        }

        var result = composite.DeepClone().AsObject();

        JsonMerger.Merge(result, source, new HashSet<string> { "template" });

        // 在任何 Remove 操作之前捕获模板原始类型，用于后续 command 类型清理判定。
        var templateType = JsonPipelineHelper.GetStringValue(result, "type");

        if (IsMultiCommandTemplate(result))
        {
            var cmdsArray = result["commands"]!.AsArray();
            result.Remove("type");
            result.Remove("commands");
            result["__multi_command"] = PlaceholderResolver.ExpandMultiObjectItems(cmdsArray, source, errors);
        }

        if (result.ContainsKey("controls") && result["controls"] is JsonArray controlsArray)
        {
            result.Remove("controls");
            result["__multi_control"] = PlaceholderResolver.ExpandMultiObjectItems(controlsArray, source, errors);
        }

        if (composite.TryGetPropertyValue("parameters", out var paramsNode) &&
            paramsNode is JsonObject parameters)
        {
            PlaceholderResolver.ApplyParameters(result, parameters, source, errors);
        }

        // A·布局完整性：模板 defaults 合并不再仅限 command 模板——控制模板（如 title-bar-with-text）
        // 的默认 layout 此前未并入最终配置，导致仅引用模板的控件（如 titleLabel）最终无 layout，
        // ECS 布局真源缺失 → 物理 0x0（静默布局缺失）。command 模板合并位置/语义保持不变。
        if (result.TryGetPropertyValue("defaults", out var defaultsNode) &&
            defaultsNode is JsonObject defaults)
        {
            foreach (var kv in defaults)
            {
                if (!result.ContainsKey(kv.Key))
                    result[kv.Key] = kv.Value?.DeepClone();
            }
        }

        // command 和 command-group 模板在应用 defaults/parameters 后清理这些驱动字段，
        // 避免残留到运行时配置。使用 templateType（原始类型）而非 result["type"]，
        // 因为 command-group 的 type 已在上方被 Remove。
        if (templateType == "command" || templateType == "command-group")
        {
            result.Remove("type");
            result.Remove("defaults");
            result.Remove("parameters");
        }

        return result;
    }

    private static bool IsMultiCommandTemplate(JsonObject obj)
    {
        return obj.TryGetPropertyValue("type", out var typeNode) &&
               typeNode?.GetValue<string>() == "command-group" &&
               obj.ContainsKey("commands") &&
               obj["commands"] is JsonArray;
    }

    /// <summary>
    /// buildEvents 落地：当控件顶层声明 <c>functionName</c>（如 action-button 模板的简化写法）且未显式声明
    /// <c>events</c> 时，合成 <c>events.click</c> + <c>bindings.parameters</c>/<c>bindings.output</c>，并清理顶层驱动字段，
    /// 使模板按钮与显式 <c>events.bindings</c> 写法在运行时完全等价（统一经 <c>events.bindings</c> 消费）。
    /// 显式 <c>events</c> 已存在时优先保留，不覆盖（用户显式配置优先）。
    /// </summary>
    /// <returns>是否修改了 <paramref name="control"/>（合成事件或清理标记）。</returns>
    public static bool BuildEventsFromTemplate(JsonObject control)
    {
        if (control == null) return false;

        // 显式 events 优先：已声明则不再合成，避免覆盖用户显式配置。
        if (control.ContainsKey("events")) return false;

        if (!control.TryGetPropertyValue("functionName", out var fnNode) ||
            fnNode is not JsonValue fnValue ||
            !fnValue.TryGetValue<string>(out var functionName) ||
            string.IsNullOrWhiteSpace(functionName))
        {
            // 无 functionName：无事件可合成；仅清理可能残留的 buildEvents 标记。
            return control.Remove("buildEvents");
        }

        var events = new JsonObject { ["click"] = functionName };

        var bindings = new JsonObject();
        if (control["parameters"] is JsonArray parameters && parameters.Count > 0)
            bindings["parameters"] = parameters.DeepClone();
        if (control.TryGetPropertyValue("output", out var outNode) &&
            outNode is JsonValue outValue && outValue.TryGetValue<string>(out var output) &&
            !string.IsNullOrWhiteSpace(output))
        {
            bindings["output"] = output;
        }

        if (bindings.Count > 0)
            events["bindings"] = bindings;

        control["events"] = events;

        // 消费后清理顶层驱动字段，避免 ControlConfig.FunctionName 等残路造成重复绑定。
        control.Remove("functionName");
        control.Remove("parameters");
        control.Remove("output");
        control.Remove("buildEvents");

        return true;
    }

}
