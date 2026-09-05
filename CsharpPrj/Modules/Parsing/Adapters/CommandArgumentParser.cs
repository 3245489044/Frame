namespace Modules.Parsing.Adapters;

public class ParsedCommandArguments
{
    public string Identifier { get; set; } = string.Empty;

    public string? Value { get; set; }

    public Dictionary<string, object> ExtraParameters { get; set; } = new();
}

public static class CommandArgumentParser
{
    public static ParsedCommandArguments Parse(string? target, Dictionary<string, object>? parameters)
    {
        var result = new ParsedCommandArguments();

        if (parameters != null && parameters.TryGetValue("value", out var valueObj) && valueObj != null)
        {
            result.Value = valueObj.ToString();
            result.Identifier = target ?? string.Empty;
            result.ExtraParameters = new Dictionary<string, object>(parameters);
            result.ExtraParameters.Remove("value");
            return result;
        }

        if (string.IsNullOrEmpty(target))
        {
            result.Identifier = string.Empty;
            result.Value = null;
            result.ExtraParameters = parameters != null
                ? new Dictionary<string, object>(parameters)
                : new Dictionary<string, object>();
            return result;
        }

        var trimmedTarget = target.Trim();
        var spaceIndex = trimmedTarget.IndexOf(' ');
        if (spaceIndex > 0)
        {
            result.Identifier = trimmedTarget.Substring(0, spaceIndex);
            result.Value = trimmedTarget.Substring(spaceIndex + 1).Trim();
        }
        else
        {
            result.Identifier = trimmedTarget;
            result.Value = null;
        }

        if (result.Value == null && parameters != null && parameters.Count > 0)
        {
            var firstNonValueParam = parameters
                .Where(p => !string.Equals(p.Key, "value", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Value?.ToString())
                .FirstOrDefault(v => v != null);

            if (firstNonValueParam != null)
            {
                result.Value = firstNonValueParam;
            }
        }

        result.ExtraParameters = parameters != null
            ? new Dictionary<string, object>(parameters)
            : new Dictionary<string, object>();
        return result;
    }

    public static string? ExtractValueOnly(string? target, Dictionary<string, object>? parameters)
    {
        return Parse(target, parameters).Value;
    }

    public static string ExtractIdentifier(string? target, Dictionary<string, object>? parameters)
    {
        return Parse(target, parameters).Identifier;
    }
}
