
using CommandFramework.Abstractions;
using CommandFramework.Models;
using Microsoft.Extensions.Logging;

namespace CommandFramework.Invoke.Handlers;

public static class ClickCommandHandler
{
    public static async Task HandleClick(
        CommandContext context,
        string? target,
        string stageName,
        Func<string, string, BindingResult?>? bindingFinder,
        IParameterExecutionWorkflow? parameterExecutionWorkflow,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(target))
        {
            context.SetStageResult(stageName, false, "click requires target");
            return;
        }

        if (parameterExecutionWorkflow == null)
        {
            context.SetStageResult(stageName, false,
                "Parameter execution workflow is not configured. Cannot execute click command. " +
                "Ensure IParameterExecutionWorkflow is injected via Options.");
            return;
        }

        var eventType = "click";
        if (context.SharedData.TryGetValue("_originalEventType", out var et))
        {
            var etStr = et?.ToString();
            if (!string.IsNullOrEmpty(etStr))
                eventType = etStr;
        }
        var binding = FindBinding(target, eventType, bindingFinder);
        if (binding == null)
        {
            var message = bindingFinder == null
                ? $"No binding finder configured. Cannot find {eventType} binding for '{target}'."
                : $"No {eventType} binding found for '{target}'.";
            context.SetStageResult(stageName, false, message);
            return;
        }

        var functionName = binding.Value.FunctionName;
        if (string.IsNullOrEmpty(functionName))
        {
            context.SetStageResult(stageName, false,
                $"No function name found in binding for '{target}'");
            return;
        }

        try
        {
            context.SharedData.TryGetValue("TargetForm", out var formObj);
            var request = new ParameterExecutionRequest
            {
                FunctionName = binding.Value.FunctionName,
                InputControls = binding.Value.Parameter,
                OutputTarget = binding.Value.Target,
                IsGuiMode = context.IsGuiMode,
                TargetForm = formObj,
                VirtualStore = context.VirtualStore,
                ControlFinder = null,
                ControlValueWriter = null
            };

            var result = await parameterExecutionWorkflow.ExecuteAsync(request, cancellationToken);

            if (result.Success)
            {
                context.FunctionResultValue = result.Value;
                context.FunctionExecutionTime = result.ExecutionTime;
                context.SetStageResult(stageName, true);
            }
            else
            {
                context.SetStageResult(stageName, false, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            context.SetStageResult(stageName, false,
                $"Parameter execution workflow error: {ex.Message}");
        }
    }

    private static BindingResult? FindBinding(
        string controlName,
        string eventType,
        Func<string, string, BindingResult?>? bindingFinder)
    {
        if (bindingFinder != null)
        {
            var result = bindingFinder(controlName, eventType);
            if (result == null)
            {
                CommandFramework.Diagnostics.CommandFrameworkDiagnosticContext.Record("binding.find.null", new { controlName, eventType });
            }
            return result;
        }

        return null;
    }
}
