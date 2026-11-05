
namespace Modules.EventFramework.Models;

public class EventRequest
{
    public string Source { get; init; } = string.Empty;

    public string EventType { get; init; } = string.Empty;

    public string? TargetName { get; init; }

    public object? Data { get; init; }

    public Dictionary<string, object>? Parameters { get; init; }

    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;

    public static EventRequest FromConfig(string eventType, string? targetName = null, object? data = null)
        => new() { Source = "config", EventType = eventType, TargetName = targetName, Data = data };

    public static EventRequest FromCode(string eventType, string? targetName = null, object? data = null)
        => new() { Source = "code", EventType = eventType, TargetName = targetName, Data = data };

    public static EventRequest FromCommand(string eventType, string? targetName = null, Dictionary<string, object>? parameters = null)
        => new() { Source = "command", EventType = eventType, TargetName = targetName, Parameters = parameters };
}
