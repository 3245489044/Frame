
using System.Text.Json.Nodes;
using Modules.Pipeline;
using TConfig;
using Common.PlaceholderFramework.TemplateExpansion.Abstractions;

namespace Common.PlaceholderFramework.TemplateExpansion.Converters;

public class ConfigValidateConverter : JsonConverterBase
{
    public bool StrictMode { get; set; } = true;

    public ConfigValidateConverter()
        : base("ConfigValidate", 900, false)
    {
    }

    protected override JsonObject? Convert(JsonObject json, ErrorCollector errors, PipelineContext context)
    {
        var errorLevel = StrictMode ? ErrorLevel.Error : ErrorLevel.Warning;

        ValidateRoot(json, errors, errorLevel);

        if (json.ContainsKey("views"))
        {
            ValidateViews(json["views"]!.AsArray(), errors, errorLevel);
        }

        if (json.ContainsKey("steps"))
        {
            ValidateSteps(json["steps"]!.AsArray(), errors, errorLevel);
        }

        if (json.ContainsKey("commands"))
        {
            ValidateCommands(json["commands"]!.AsArray(), errors, errorLevel);
        }

        return null;
    }

    private void ValidateRoot(JsonObject json, ErrorCollector errors, ErrorLevel errorLevel)
    {
        var hasContent = json.ContainsKey("views") ||
                         json.ContainsKey("steps") ||
                         json.ContainsKey("commands") ||
                         json.ContainsKey("config");

        if (!hasContent)
        {
            errors.Add("VALIDATE_NO_CONTENT",
                "JSON configuration must contain at least one of: views, steps, commands, config",
                errorLevel);
        }
    }

    private void ValidateViews(JsonArray views, ErrorCollector errors, ErrorLevel errorLevel)
    {
        for (int i = 0; i < views.Count; i++)
        {
            var view = views[i]?.AsObject();
            if (view == null)
            {
                errors.Add($"VALIDATE_VIEW_{i}_NULL",
                    $"View at index {i} is null",
                    errorLevel);
                continue;
            }

            if (!view.ContainsKey("type"))
            {
                errors.Add($"VALIDATE_VIEW_{i}_NO_TYPE",
                    $"View at index {i} is missing required field 'type'",
                    errorLevel);
            }

            if (view.ContainsKey("controls"))
            {
                ValidateControls(view["controls"]!.AsArray(), errors, errorLevel, $"view[{i}]");
            }
        }
    }

    private void ValidateControls(JsonArray controls, ErrorCollector errors, ErrorLevel errorLevel, string path)
    {
        for (int i = 0; i < controls.Count; i++)
        {
            var control = controls[i]?.AsObject();
            if (control == null)
            {
                errors.Add($"VALIDATE_CONTROL_{path}_{i}_NULL",
                    $"Control at {path}[{i}] is null",
                    errorLevel);
                continue;
            }

            if (!control.ContainsKey("type"))
            {
                errors.Add($"VALIDATE_CONTROL_{path}_{i}_NO_TYPE",
                    $"Control at {path}[{i}] is missing required field 'type'",
                    errorLevel);
            }

            foreach (var key in JsonPipelineHelper.ChildControlKeys)
            {
                if (control.ContainsKey(key) && control[key] is JsonArray childArray)
                {
                    ValidateControls(childArray, errors, errorLevel, $"{path}[{i}].{key}");
                }
            }
        }
    }

    private void ValidateSteps(JsonArray steps, ErrorCollector errors, ErrorLevel errorLevel)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i]?.AsObject();
            if (step == null)
            {
                errors.Add($"VALIDATE_STEP_{i}_NULL",
                    $"Step at index {i} is null",
                    errorLevel);
                continue;
            }

            if (!step.ContainsKey("name"))
            {
                errors.Add($"VALIDATE_STEP_{i}_NO_NAME",
                    $"Step at index {i} is missing required field 'name'",
                    errorLevel);
            }
        }
    }

    private void ValidateCommands(JsonArray commands, ErrorCollector errors, ErrorLevel errorLevel)
    {
        for (int i = 0; i < commands.Count; i++)
        {
            var command = commands[i]?.AsObject();
            if (command == null)
            {
                errors.Add($"VALIDATE_COMMAND_{i}_NULL",
                    $"Command at index {i} is null",
                    errorLevel);
                continue;
            }

            if (!command.ContainsKey("action"))
            {
                errors.Add($"VALIDATE_COMMAND_{i}_NO_ACTION",
                    $"Command at index {i} is missing required field 'action'",
                    errorLevel);
            }
        }
    }
}
