
using System.Text.Json;
using System.Text.Json.Serialization;
using Modules.DiagnosticContext.Core;

namespace Modules.DiagnosticContext.Formatting;

public class JsonFormatter : IContextFormatter
{
    public static readonly JsonFormatter Default = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Format(ScopeNode scope)
    {
        var dto = ToDto(scope);
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    private static DiagnosticDto ToDto(ScopeNode node)
    {
        return new DiagnosticDto
        {
            Name = node.Name,
            CreatedAt = node.CreatedAt,
            ElapsedMs = (long)(DateTime.UtcNow - node.CreatedAt).TotalMilliseconds,
            Events = node.Events.Select(e => new EventDto
            {
                EventName = e.EventName,
                RelativeMs = e.RelativeMs,
                Timestamp = e.Timestamp,
                Data = e.RawData
            }).ToList(),
            State = node.State.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString()),
            Scopes = node.Children.Select(ToDto).ToList()
        };
    }


    private class DiagnosticDto
    {
        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public long ElapsedMs { get; set; }
        public List<EventDto> Events { get; set; } = new();
        public Dictionary<string, string?> State { get; set; } = new();
        public List<DiagnosticDto> Scopes { get; set; } = new();
    }

    private class EventDto
    {
        public string EventName { get; set; } = "";
        public long RelativeMs { get; set; }
        public string Timestamp { get; set; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Data { get; set; }
    }
}
