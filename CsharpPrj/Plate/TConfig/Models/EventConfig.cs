
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TConfig;

[JsonConverter(typeof(EventConfigConverter))]
public class EventConfig
{
    public Dictionary<string, List<string>> EventsDictionary { get; set; } = new();

    public EventBinding? Bindings { get; set; }
}

public class EventConfigConverter : JsonConverter<EventConfig>
{
    private static readonly HashSet<string> KnownNonEventProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "bindings"
    };

    public override EventConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject");

        var config = new EventConfig();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return config;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected PropertyName");

            var propertyName = reader.GetString() ?? "";
            reader.Read();

            var lowerName = propertyName.ToLowerInvariant();

            if (KnownNonEventProperties.Contains(lowerName))
            {
                switch (lowerName)
                {
                    case "bindings":
                        config.Bindings = JsonSerializer.Deserialize<EventBinding>(ref reader, options);
                        break;
                }
            }
            else
            {
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    var functions = JsonSerializer.Deserialize<List<string>>(ref reader, options);
                    if (functions != null && functions.Count > 0)
                        config.EventsDictionary[propertyName] = functions;
                }
                else if (reader.TokenType == JsonTokenType.String)
                {
                    var funcName = reader.GetString();
                    if (!string.IsNullOrEmpty(funcName))
                        config.EventsDictionary[propertyName] = new List<string> { funcName };
                }
                else
                {
                    reader.Skip();
                }
            }
        }

        throw new JsonException("Expected EndObject");
    }

    public override void Write(Utf8JsonWriter writer, EventConfig value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        if (value.Bindings != null)
        {
            writer.WritePropertyName("bindings");
            JsonSerializer.Serialize(writer, value.Bindings, options);
        }

        foreach (var kvp in value.EventsDictionary)
        {
            if (kvp.Value.Count == 1)
            {
                writer.WriteString(kvp.Key, kvp.Value[0]);
            }
            else
            {
                writer.WritePropertyName(kvp.Key);
                JsonSerializer.Serialize(writer, kvp.Value, options);
            }
        }

        writer.WriteEndObject();
    }
}
