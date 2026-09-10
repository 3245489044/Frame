
using System;
using System.Collections.Generic;
using TConfig;

namespace Share.EventBindingOrchestrator.Normalization;

public sealed class NormalizedEventBinding
{
    public string ControlName { get; init; } = string.Empty;

    public string EventName { get; init; } = string.Empty;

    public string FunctionName { get; init; } = string.Empty;

    public IReadOnlyList<string> Parameters { get; init; } = Array.Empty<string>();

    public string? OutputTarget { get; init; }

    public string? OutputKey { get; init; }

    public bool IsSimpleFormat => Parameters.Count == 0 && OutputTarget == null;
}

public static class JsonEventBindingConverter
{
    public static IReadOnlyList<NormalizedEventBinding> Convert(EventConfig? events, string? controlName = null)
    {
        if (events == null)
            return Array.Empty<NormalizedEventBinding>();

        var result = new List<NormalizedEventBinding>();


        foreach (var kvp in events.EventsDictionary)
        {
            var eventName = kvp.Key;
            foreach (var funcName in kvp.Value)
            {
                if (string.IsNullOrWhiteSpace(funcName))
                    continue;

                result.Add(new NormalizedEventBinding
                {
                    ControlName = controlName ?? string.Empty,
                    EventName = eventName,
                    FunctionName = funcName
                });
            }
        }

        return result;
    }
}