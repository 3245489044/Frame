
using System.Text.Json.Serialization;

namespace TConfig;

public class EventRequest
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("controlName")]
    public string? ControlName { get; set; }

    [JsonIgnore]
    public object? Data { get; set; }

    [JsonPropertyName("parameters")]
    public Dictionary<string, object>? Parameters { get; set; }

    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonIgnore]
    public object? Config { get; set; }

    public static EventRequest FromConfig(string eventType, string? controlName, object? data = null)
        => new() { Source = "config", EventType = eventType, ControlName = controlName, Data = data };

    public static EventRequest FromCode(string eventType, string? controlName, object? data = null)
        => new() { Source = "code", EventType = eventType, ControlName = controlName, Data = data };

    public static EventRequest FromCommand(string eventType, string? controlName, Dictionary<string, object>? parameters = null)
        => new() { Source = "command", EventType = eventType, ControlName = controlName, Parameters = parameters };

    public static EventRequest FromResize(string? controlName, int width, int height)
        => new()
        {
            Source = "resize",
            EventType = "resize",
            ControlName = controlName,
            Parameters = new Dictionary<string, object>
            {
                ["Width"] = width,
                ["Height"] = height
            }
        };
}
