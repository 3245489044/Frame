
using Container.Abstractions;
using CommandFramework.Models;
using Microsoft.Extensions.Logging;

namespace CommandFramework.Invoke.Handlers;

public static class NonEventCommandHandler
{
    public static async Task HandleNonEvent(
        CommandContext context,
        string action,
        string? target,
        string stageName,
        INonEventCommandLookup? nonEventCommandLookup,
        ILogger? logger = null)
    {
        try
        {
            if (nonEventCommandLookup != null)
            {
                if (nonEventCommandLookup.TryGetHandler(action, out var handler))
                {
                    await HandlerInvoker.InvokeAsync(handler, context);

                    if (context.HasError)
                    {
                        return;
                    }

                    context.SetStageResult(stageName, true);
                    return;
                }
            }

            context.SetStageResult(stageName, false,
                nonEventCommandLookup == null
                    ? $"Non-event command lookup is not configured. Cannot handle '{action}'."
                    : $"Unknown non-event command: '{action}'. Ensure it is registered in NonEventRegistry.");
        }
        catch (Exception ex)
        {
            context.SetStageResult(stageName, false,
                $"Non-event command '{action}' execution error: {ex.Message}");
        }
    }
}
