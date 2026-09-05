namespace Common.SystemJsonFramework
{
#nullable disable
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.Json.Serialization;
    using System.IO;

    public static class Serialization
    {
        public static string SerializeToJson(object obj)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            return System.Text.Json.JsonSerializer.Serialize(obj, options);
        }

        public static class SafeJsonSerializer
        {
            private static readonly JsonSerializerOptions DefaultSafeOptions = CreateSafeOptions();

            public static string Serialize(object obj, JsonSerializerOptions jsonSerializerOptions = null)
            {
                var options = jsonSerializerOptions ?? DefaultSafeOptions;
                var cleanedObject = CleanUnserializableProperties(obj);
                return System.Text.Json.JsonSerializer.Serialize(cleanedObject, options);
            }

            private static JsonSerializerOptions CreateSafeOptions()
            {
                return new JsonSerializerOptions
                {
                    WriteIndented = false,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    IgnoreReadOnlyProperties = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new SafeObjectConverter() }
                };
            }

            private static object CleanUnserializableProperties(object obj)
            {
                if (obj == null) return null;

                var type = obj.GetType();

                if (TypeHelper.IsSimpleType(type))
                    return obj;

                if (obj is IEnumerable enumerable && !(obj is string))
                {
                    return ProcessEnumerable(enumerable);
                }

                return ProcessComplexObject(obj);
            }

            private static object ProcessEnumerable(IEnumerable enumerable)
            {
                var list = new List<object>();
                foreach (var item in enumerable)
                {
                    list.Add(CleanUnserializableProperties(item));
                }
                return list;
            }

            private static object ProcessComplexObject(object obj)
            {
                var type = obj.GetType();
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && !TypeHelper.IsUnserializableType(p.PropertyType));

                var dictionary = new Dictionary<string, object>();

                foreach (var prop in properties)
                {
                    var value = prop.GetValue(obj);
                    dictionary[ToCamelCase(prop.Name)] = CleanUnserializableProperties(value);
                }

                return dictionary;
            }

            private static string ToCamelCase(string name)
            {
                if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
                    return name;

                return char.ToLowerInvariant(name[0]) + name.Substring(1);
            }
        }

        public class SafeObjectConverter : JsonConverter<object>
        {
            public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotImplementedException("此转换器仅用于序列化，不支持反序列化");
            }

            public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
            {
                if (value == null)
                {
                    writer.WriteNullValue();
                    return;
                }

                var type = value.GetType();

                if (TypeHelper.IsSimpleType(type))
                {
                    WriteSimpleType(writer, value, type);
                    return;
                }

                if (value is IEnumerable enumerable && !(value is string))
                {
                    WriteEnumerable(writer, enumerable, options);
                    return;
                }

                WriteComplexObject(writer, value, options);
            }

            private static void WriteSimpleType(Utf8JsonWriter writer, object value, Type type)
            {
                switch (value)
                {
                    case string str:
                        writer.WriteStringValue(str);
                        break;
                    case DateTime dateTime:
                        writer.WriteStringValue(dateTime);
                        break;
                    case DateTimeOffset dateTimeOffset:
                        writer.WriteStringValue(dateTimeOffset);
                        break;
                    case decimal dec:
                        writer.WriteNumberValue(dec);
                        break;
                    case Guid guid:
                        writer.WriteStringValue(guid);
                        break;
                    case TimeSpan timeSpan:
                        writer.WriteStringValue(timeSpan.ToString());
                        break;
                    case Enum enumValue:
                        writer.WriteStringValue(enumValue.ToString());
                        break;
                    default:
                        System.Text.Json.JsonSerializer.Serialize(writer, value, type);
                        break;
                }
            }

            private static void WriteEnumerable(Utf8JsonWriter writer, IEnumerable enumerable, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                foreach (var item in enumerable)
                {
                    if (item == null)
                    {
                        writer.WriteNullValue();
                    }
                    else
                    {
                        var converter = new SafeObjectConverter();
                        converter.Write(writer, item, options);
                    }
                }
                writer.WriteEndArray();
            }

            private static void WriteComplexObject(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
            {
                var type = value.GetType();
                writer.WriteStartObject();

                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && !TypeHelper.IsUnserializableType(p.PropertyType));

                foreach (var prop in properties)
                {
                    var propValue = prop.GetValue(value);
                    var propertyName = options.PropertyNamingPolicy?.ConvertName(prop.Name) ?? prop.Name;

                    writer.WritePropertyName(propertyName);

                    var converter = new SafeObjectConverter();
                    converter.Write(writer, propValue, options);
                }

                writer.WriteEndObject();
            }
        }

        private static class TypeHelper
        {
            public static bool IsSimpleType(Type type)
            {
                if (type == null) return false;

                var underlyingType = Nullable.GetUnderlyingType(type);
                if (underlyingType != null)
                {
                    return IsSimpleType(underlyingType);
                }

                return type.IsPrimitive ||
                       type == typeof(string) ||
                       type == typeof(DateTime) ||
                       type == typeof(DateTimeOffset) ||
                       type == typeof(decimal) ||
                       type == typeof(Guid) ||
                       type == typeof(TimeSpan) ||
                       type.IsEnum;
            }

            public static bool IsUnserializableType(Type type)
            {
                if (type == null) return false;

                var underlyingType = Nullable.GetUnderlyingType(type);
                if (underlyingType != null)
                {
                    return IsUnserializableType(underlyingType);
                }

                if (typeof(Delegate).IsAssignableFrom(type)) return true;
                if (typeof(Task).IsAssignableFrom(type)) return true;
                if (type.FullName?.StartsWith("System.Reflection") == true) return true;
                if (type.FullName?.StartsWith("System.Threading") == true) return true;
                if (typeof(Stream).IsAssignableFrom(type)) return true;

                return false;
            }
        }
    }

    public static class Deserialization
    {
        public static T Deserialize<T>(string json, JsonSerializerOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(json))
                return default(T);

            try
            {
                JsonNode jsonNode = ParseJsonNodeWithErrorHandling<T>(json);
                if (jsonNode == null)
                    return default(T);

                var result = DeserializeWithErrorHandling<T>(json, options);
                return result;
            }
            catch (Exception ex)
            {
                LogDeserializationError<T>("反序列化过程中发生未预期的异常", ex, json);
                return default(T);
            }
        }

        private static JsonNode ParseJsonNodeWithErrorHandling<T>(string json)
        {
            try
            {
                return JsonNode.Parse(json);
            }
            catch (JsonException ex)
            {
                LogDeserializationWarning<T>($"JSON格式错误 - {ex.Message}", json);
                return null;
            }
            catch (Exception ex)
            {
                LogDeserializationWarning<T>($"其他异常 - {ex.GetType().Name}: {ex.Message}", json);
                return null;
            }
        }

        private static T DeserializeWithErrorHandling<T>(string json, JsonSerializerOptions options = null)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(json, options);
            }
            catch (JsonException ex)
            {
                LogDeserializationWarning<T>($"反序列化对象失败 - {ex.GetType().Name}: {ex.Message}", json);
                return default(T);
            }
            catch (Exception ex)
            {
                LogDeserializationWarning<T>($"反序列化对象失败 - {ex.GetType().Name}: {ex.Message}", json);
                return default(T);
            }
        }

        private static void LogDeserializationWarning<T>(string message, string json = null)
        {
            string logMessage = $"Deserialize<{typeof(T).Name}>: {message}";
            if (!string.IsNullOrEmpty(json))
            {
                logMessage += $"\nJSON内容: {json}";
            }

        }

        private static void LogDeserializationError<T>(string message, Exception ex, string json = null)
        {
            string logMessage = $"Deserialize<{typeof(T).Name}> {message}: {ex.Message}\n{ex.StackTrace}";
            if (!string.IsNullOrEmpty(json))
            {
                logMessage += $"\nJSON内容: {json}";
            }

        }
    }

    public static class JsonHelper
    {
        private static readonly Lazy<JsonSerializerOptions> InternalOptions = new(() => new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        });

        private static readonly Lazy<JsonSerializerOptions> DisplayOptions = new(() => new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        public static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, InternalOptions.Value);
        }

        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return default;

            return JsonSerializer.Deserialize<T>(json, InternalOptions.Value);
        }

        public static string SerializeForDisplay<T>(T value)
        {
            return JsonSerializer.Serialize(value, DisplayOptions.Value);
        }

        public static JsonDocument ParseDocument(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonDocument.Parse(json);
        }

        public static string PrettyPrint(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return json;

            try
            {
                var doc = JsonDocument.Parse(json);
                using var stream = new System.IO.MemoryStream();
                using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                doc.WriteTo(writer);
                writer.Flush();
                return System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }
            catch
            {
                return json;
            }
        }
    }
}
