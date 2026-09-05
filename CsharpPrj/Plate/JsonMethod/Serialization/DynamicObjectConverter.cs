
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plate.JsonMethod.Serialization
{
    public class DynamicObjectConverter : JsonConverter<object>
    {
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.StartObject => ReadObject(ref reader, options),
                JsonTokenType.StartArray => ReadArray(ref reader, options),
                JsonTokenType.Null => null,
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.Number when reader.TryGetInt64(out var l) => l,
                JsonTokenType.Number => reader.GetDouble(),
                JsonTokenType.String => reader.GetString(),
                _ => JsonSerializer.Deserialize(ref reader, typeof(object), options)
            };
        }

        private Dictionary<string, object?> ReadObject(ref Utf8JsonReader reader,
            JsonSerializerOptions options)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var key = reader.GetString()!;
                    reader.Read();
                    dict[key] = Read(ref reader, typeof(object), options);
                }
            }
            return dict;
        }

        private List<object?> ReadArray(ref Utf8JsonReader reader,
            JsonSerializerOptions options)
        {
            var list = new List<object?>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                list.Add(Read(ref reader, typeof(object), options));
            }
            return list;
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value is Dictionary<string, object?> dict)
            {
                writer.WriteStartObject();
                foreach (var kv in dict)
                {
                    writer.WritePropertyName(kv.Key);
                    WriteValue(writer, kv.Value, options);
                }
                writer.WriteEndObject();
            }
            else if (value is List<object?> list)
            {
                writer.WriteStartArray();
                foreach (var item in list)
                {
                    WriteValue(writer, item, options);
                }
                writer.WriteEndArray();
            }
            else
            {
                JsonSerializer.Serialize(writer, value, value?.GetType() ?? typeof(object), options);
            }
        }

        private void WriteValue(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
        {
            if (value == null)
                writer.WriteNullValue();
            else
                Write(writer, value, options);
        }
    }
}
