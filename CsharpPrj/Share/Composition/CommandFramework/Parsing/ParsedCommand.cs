
using CommandFramework.Abstractions;

namespace CommandFramework.Parsing;

public class ParsedCommand : IParsedCommand
{
    public string Verb { get; set; } = string.Empty;

    public string? Action { get; set; }

    public string? Target { get; set; }

    public Dictionary<string, object>? Parameters { get; set; }

    public string? NormalizedCommand { get; set; }

    public bool IsNonEventCommand { get; set; }

    public bool IsComment { get; set; }

    public bool IsSuccess { get; set; } = false;

    public string? ErrorMessage { get; set; }

    string? IParsedCommand.RawCommand => NormalizedCommand;

    IReadOnlyDictionary<string, object>? IParsedCommand.Parameters => Parameters;
}

public enum JsonVerb
{
    Do,
    Get,
    Look,
    Wait,
    Assert
}

public record JsonCommand
{
    public JsonVerb Verb { get; init; }

    public string? Action { get; init; }

    public string? Target { get; init; }

    public Dictionary<string, object?>? Parameters { get; init; }

    public string? OriginalText { get; init; }

    public Guid CommandId { get; init; } = Guid.NewGuid();

    public static JsonCommand FromParsedCommand(ParsedCommand parsed, string? originalText = null)
    {
        if (parsed == null)
            throw new ArgumentNullException(nameof(parsed));

        var verb = parsed.Verb?.ToLowerInvariant() switch
        {
            "do" => JsonVerb.Do,
            "get" => JsonVerb.Get,
            "look" => JsonVerb.Look,
            "wait" => JsonVerb.Wait,
            "assert" => JsonVerb.Assert,
            _ => JsonVerb.Do
        };

        return new JsonCommand
        {
            Verb = verb,
            Action = parsed.Action,
            Target = parsed.Target,
            Parameters = parsed.Parameters != null
                ? ConvertParameters(parsed.Parameters)
                : null,
            OriginalText = originalText ?? parsed.NormalizedCommand
        };
    }

    private static Dictionary<string, object?>? ConvertParameters(Dictionary<string, object> parameters)
    {
        if (parameters == null || parameters.Count == 0)
            return null;

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in parameters)
        {
            if (kvp.Value is not string strValue)
            {
                result[kvp.Key] = kvp.Value;
                continue;
            }

            result[kvp.Key] = InferValue(strValue);
        }
        return result;
    }

    public static object? InferValue(string val)
    {
        if (string.IsNullOrEmpty(val))
            return val;

        if (bool.TryParse(val, out var boolVal))
            return boolVal;

        if (string.Equals(val, "null", StringComparison.OrdinalIgnoreCase))
            return null;

        if (long.TryParse(val, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var longVal))
            return longVal;

        if (double.TryParse(val, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
            System.Globalization.CultureInfo.InvariantCulture, out var doubleVal))
        {
            if (val.Contains('.') && decimal.TryParse(val,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var decVal))
                return decVal;
            return doubleVal;
        }

        if (DateTime.TryParse(val, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dtVal))
            return dtVal;

        if (DateTimeOffset.TryParse(val, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dtoVal))
            return dtoVal;

        if (Guid.TryParse(val, out var guidVal))
            return guidVal;

        return val;
    }
}