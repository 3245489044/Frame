using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;


namespace Modules.DiagnosticContext.Formatting;

public static class JsonOptions
{
    public static readonly JsonSerializerOptions CompactRelaxed = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static readonly JsonSerializerOptions UnsafeRelaxed = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static JsonSerializerOptions UnsafeRelaxedWithConverter<TConverter>(TConverter converter) where TConverter : JsonConverter
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(converter);
        return options;
    }
}
