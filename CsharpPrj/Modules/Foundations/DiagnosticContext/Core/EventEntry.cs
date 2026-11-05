
using System.Text.Json;
using Modules.DiagnosticContext.Formatting;
using System.Text.Json.Serialization;

namespace Modules.DiagnosticContext.Core;

public class EventEntry
{
    public string EventName { get; }

    public long RelativeMs { get; }

    [JsonIgnore]
    public object? RawData { get; }

    public string? DataJson { get; private set; }

    public EventEntry(string eventName, long relativeMs, object? data = null)
    {
        EventName = eventName ?? throw new ArgumentNullException(nameof(eventName));
        RelativeMs = relativeMs;
        RawData = data;
        DataJson = null;
    }

    public string Timestamp => $"{(RelativeMs / 60000):D2}:{(RelativeMs % 60000) / 1000:D2}.{RelativeMs % 1000:D3}";

    public string DataDisplay => GetDataJson() ?? "(null)";

    public string? GetDataJson()
    {
        if (DataJson != null) return DataJson;
        if (RawData != null)
        {
            DataJson = JsonSerializer.Serialize(RawData, JsonOptions.CompactRelaxed);
            return DataJson;
        }
        return null;
    }
}
