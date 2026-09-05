#nullable disable

namespace Common.NewtonSoftJsonFramework
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    public class DictionaryConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(object) ||
                   objectType == typeof(Dictionary<string, object>) ||
                   objectType == typeof(IDictionary<string, object>);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return ReadValue(reader);
        }

        private object ReadValue(JsonReader reader)
        {
            return reader.TokenType switch
            {
                JsonToken.StartObject => ReadObject(reader),
                JsonToken.StartArray => ReadArray(reader),
                JsonToken.Null => null,
                _ => reader.Value
            };
        }

        private Dictionary<string, object> ReadObject(JsonReader reader)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            while (reader.Read() && reader.TokenType != JsonToken.EndObject)
            {
                if (reader.TokenType == JsonToken.PropertyName)
                {
                    string key = reader.Value?.ToString() ?? string.Empty;
                    reader.Read();
                    dict[key] = ReadValue(reader);
                }
            }

            return dict;
        }

        private List<object> ReadArray(JsonReader reader)
        {
            var list = new List<object>();

            while (reader.Read() && reader.TokenType != JsonToken.EndArray)
            {
                list.Add(ReadValue(reader));
            }

            return list;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            WriteValue(writer, value);
        }

        private void WriteValue(JsonWriter writer, object value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            switch (value)
            {
                case Dictionary<string, object> dict:
                    WriteObject(writer, dict);
                    break;
                case List<object> list:
                    WriteArray(writer, list);
                    break;
                case JValue jValue:
                    writer.WriteValue(jValue.Value);
                    break;
                default:
                    writer.WriteValue(value);
                    break;
            }
        }

        private void WriteObject(JsonWriter writer, Dictionary<string, object> dict)
        {
            writer.WriteStartObject();

            foreach (var kvp in dict)
            {
                writer.WritePropertyName(kvp.Key);
                WriteValue(writer, kvp.Value);
            }

            writer.WriteEndObject();
        }

        private void WriteArray(JsonWriter writer, List<object> list)
        {
            writer.WriteStartArray();

            foreach (var item in list)
            {
                WriteValue(writer, item);
            }

            writer.WriteEndArray();
        }
    }
}
