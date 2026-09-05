
using CommandFramework.Models;
using FrameworkAbstractions.Controls;
using Microsoft.Extensions.Logging;

namespace CommandFramework.Invoke.Handlers;

public static class SetCommandHandler
{
    public static Task HandleSet(
        CommandContext context,
        string? target,
        string stageName,
        IControlFinder? controlFinder,
        TConfig.Controls.IControlValueAccessor? controlValueWriter,
        ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(target))
        {
            context.SetStageResult(stageName, false, "set requires target");
            return Task.CompletedTask;
        }

        var value = context.ParsedCommand?.Parameters?.TryGetValue("value", out var v) == true
            ? v?.ToString()
            : null;

        if (context.IsGuiMode && context.SharedData.TryGetValue("TargetForm", out var formObj) && formObj != null)
        {
            var finder = controlFinder;
            var writer = controlValueWriter;

            if (finder == null || writer == null)
            {
                context.SetStageResult(stageName, false,
                    "GUI mode requires IControlFinder and IControlValueWriter to be configured. " +
                    "Ensure they are injected via Options (EnableControlFinder/EnableControlValueWriter).");
                return Task.CompletedTask;
            }

            if (string.IsNullOrEmpty(value))
            {
                context.SetStageResult(stageName, false,
                    $"set '{target}' requires a non-empty value. Use 'value=...' parameter to specify the value to set.");
                return Task.CompletedTask;
            }

            var control = ControlHelper.FindControl(formObj, target, finder, logger);
            if (control != null)
            {
                ControlHelper.SetControlText(control, value, writer, logger);
            }
            else
            {
                context.SetStageResult(stageName, false,
                    $"Control '{target}' not found on the target form.");
                return Task.CompletedTask;
            }
        }

        else if (context.IsCliMode && context.VirtualStore != null)
        {
            context.VirtualStore[target] = value;
            context.SetStageResult(stageName, true);
            return Task.CompletedTask;
        }

        context.SetStageResult(stageName, false,
            "HandleSet failed: Neither GUI mode (with TargetForm) nor CLI mode (with VirtualStore) is properly configured. " +
            "Ensure the execution mode is correctly set.");
        return Task.CompletedTask;
    }
}
