
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Plate.JsonMethod.Serialization
{
    public enum JsonContext
    {
        Default,
        UIConfig,
        ScriptCommand,
        PrettyPrint,
        Compact,
        LogExport,
        LogCompact
    }

    public static class JsonSerializerFactory
    {
        public static class Contexts
        {
            private static readonly JsonSerializerOptions _lenientOptions = CreateLenientOptions();
            private static readonly JsonSerializerOptions _prettyOptions = CreatePrettyOptions();

            public static JsonSerializerOptions UIConfig => _lenientOptions;

            public static JsonSerializerOptions ScriptCommand => _lenientOptions;

            public static JsonSerializerOptions PrettyPrint => _prettyOptions;

            public static JsonSerializerOptions Compact { get; } = CreateCompactOptions();

            public static JsonSerializerOptions LogExport { get; } = CreateLogExportOptions();

            public static JsonSerializerOptions LogCompact { get; } = CreateLogCompactOptions();

            public static JsonSerializerOptions Default { get; } = CreateDefaultOptions();
        }

        public static JsonSerializerOptions GetOptions(JsonContext context)
        {
            return context switch
            {
                JsonContext.UIConfig => Contexts.UIConfig,
                JsonContext.ScriptCommand => Contexts.ScriptCommand,
                JsonContext.PrettyPrint => Contexts.PrettyPrint,
                JsonContext.Compact => Contexts.Compact,
                JsonContext.LogExport => Contexts.LogExport,
                JsonContext.LogCompact => Contexts.LogCompact,
                _ => Contexts.Default
            };
        }

        public static JsonSerializerOptions Create(
            JsonContext context = JsonContext.Default,
            Action<JsonSerializerOptions>? configure = null)
        {
            var options = new JsonSerializerOptions(GetOptions(context));
            configure?.Invoke(options);
            return options;
        }

        public static void RegisterConverter(JsonConverter converter, JsonContext context)
        {
            var options = GetOptions(context);
            options.Converters.Add(converter);
        }

        private static JsonSerializerOptions CreateBaseOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Converters = { new JsonStringEnumConverter() }
            };
        }

        private static JsonSerializerOptions CreateLenientOptions()
        {
            var options = CreateBaseOptions();
            options.WriteIndented = false;
            options.ReadCommentHandling = JsonCommentHandling.Skip;
            options.AllowTrailingCommas = true;
            return options;
        }

        private static JsonSerializerOptions CreatePrettyOptions()
        {
            var options = CreateBaseOptions();
            options.WriteIndented = true;
            return options;
        }

        private static JsonSerializerOptions CreateCompactOptions()
        {
            var options = CreateBaseOptions();
            options.WriteIndented = false;
            return options;
        }

        private static JsonSerializerOptions CreateDefaultOptions()
        {
            var options = CreateBaseOptions();
            options.WriteIndented = false;
            options.IgnoreReadOnlyProperties = true;
            return options;
        }

        private static JsonSerializerOptions CreateLogExportOptions()
        {
            var options = CreateBaseOptions();
            options.WriteIndented = true;
            options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin);
            return options;
        }

        private static JsonSerializerOptions CreateLogCompactOptions()
        {
            var options = CreateBaseOptions();
            options.WriteIndented = false;
            options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin);
            return options;
        }
    }
}
