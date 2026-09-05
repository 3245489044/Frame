
using TConfig;
using Share.EventBindingOrchestrator.Diagnostics;

using EventBindingModel = TConfig.EventBinding;

namespace Share.EventBindingOrchestrator.Parameters;

public static class EventBindingHelper
{


    public static (string? outputTarget, string? outputKey) ResolveOutput(EventBindingModel? bindings, string functionName)
    {
        if (bindings?.OutputMapping is { Count: > 0 } &&
            bindings.OutputMapping.TryGetValue(functionName, out var mappedOutput))
        {
            return (mappedOutput, bindings.OutputKey);
        }

        return (bindings?.Output, bindings?.OutputKey);
    }

    public static string? GetControlName(object control)
    {
        try
        {
            var prop = control.GetType().GetProperty("Name");
            return prop?.GetValue(control)?.ToString();
        }
        catch (Exception ex)
        {
            EventBindingDiagnosticContext.Record("eventbinder.helper.get_control_name_failed", new
            {
                controlType = control?.GetType().Name ?? "(null)",
                error = ex.Message
            });
            return null;
        }
    }

    public static string? GetExtensionDataValue(ControlConfig config, string key)
    {
        if (config.ExtensionData == null)
            return null;

        if (config.ExtensionData.TryGetValue(key, out var value) && value != null)
        {
            return value.ToString();
        }

        return null;
    }

}
