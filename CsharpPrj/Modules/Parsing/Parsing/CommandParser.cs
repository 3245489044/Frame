using System.Globalization;
using Modules.Parsing.Models;

namespace Modules.Parsing.Parsing;

public static class CommandParser
{
    public static Models.MetaCommand Parse(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command cannot be null or empty", nameof(command));

        var normalized = CommandNormalizer.Normalize(command);

        if (string.IsNullOrEmpty(normalized))
            return null;

        var effectiveCommand = normalized;

        var tokenizer = new CommandTokenizer(effectiveCommand);
        var tokens = tokenizer.Tokenize();

        if (tokens.Count == 0)
        {
            return new Models.MetaCommand
            {
                Verb = MetaVerb.Do,
                Action = "noop",
                OriginalCommand = command,
                CommandId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow
            };
        }

        var verbStr = tokens[0].ToLower();
        if (!Enum.TryParse<MetaVerb>(verbStr, true, out var verb))
        {
            return ParseAsDoCommand(tokens, effectiveCommand);
        }

        var cmd = new Models.MetaCommand
        {
            Verb = verb,
            OriginalCommand = command,
            CommandId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };

        if (tokens.Count > 1)
        {
            if (verb == MetaVerb.Do)
            {
                cmd.Action = tokens[1];
                if (tokens.Count > 2)
                {
                    ParseDoTargetAndParameters(tokens, cmd);
                }
            }
            else
            {
                cmd.Target = tokens[1];
            }
        }

        return cmd;
    }

    private static void ParseDoTargetAndParameters(List<string> tokens, Models.MetaCommand cmd)
    {
        var targetParts = new List<string>();
        var paramParts = new List<string>();
        bool jsonFound = false;

        for (int i = 2; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token.StartsWith("{") || token.StartsWith("["))
            {
                var (jsonStr, endIndex) = MergeJsonTokens(tokens, i);
                if (!string.IsNullOrEmpty(jsonStr))
                {
                    if (cmd.Parameters == null)
                        cmd.Parameters = new Dictionary<string, object>();
                    cmd.Parameters["_json"] = jsonStr;
                    jsonFound = true;
                    i = endIndex;
                    continue;
                }
            }

            if (cmd.Action == "set" && targetParts.Count == 0)
            {
                targetParts.Add(token);
            }
            else if (token.Contains('='))
            {
                paramParts.Add(token);
            }
            else
            {
                if (cmd.Action == "set")
                {
                    if (cmd.Parameters == null)
                        cmd.Parameters = new Dictionary<string, object>();
                    cmd.Parameters[token] = true;
                }
                else
                {
                    targetParts.Add(token);
                }
            }
        }

        if (targetParts.Count > 0)
            cmd.Target = string.Join(" ", targetParts);

        if (paramParts.Count > 0)
        {
            var parameters = ParameterParser.ParseParameters(paramParts, 0);
            if (parameters.Count > 0)
            {
                if (cmd.Parameters == null)
                    cmd.Parameters = new Dictionary<string, object>();
                foreach (var kv in parameters)
                {
                    cmd.Parameters[kv.Key] = kv.Value;
                }
            }
        }

        if (cmd.Target == null && !jsonFound && tokens.Count > 2)
        {
            cmd.Target = tokens[2];
        }
    }

    private static Models.MetaCommand ParseAsDoCommand(List<string> tokens, string originalCommand)
    {
        var cmd = new Models.MetaCommand
        {
            Verb = MetaVerb.Do,
            OriginalCommand = originalCommand,
            CommandId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };

        cmd.Action = tokens[0];

        if (tokens.Count > 1)
        {
            ParseDoTargetAndParameters(tokens, cmd);
        }

        return cmd;
    }

    public static (string mergedJson, int endIndex) MergeJsonTokens(List<string> tokens, int startIndex)
    {
        if (startIndex >= tokens.Count)
            return (string.Empty, startIndex);

        var firstToken = tokens[startIndex];
        if (!firstToken.StartsWith("{") && !firstToken.StartsWith("["))
        {
            return (tokens[startIndex], startIndex);
        }

        var openChar = firstToken[0];
        var closeChar = openChar == '{' ? '}' : ']';
        int depth = 0;
        var jsonParts = new List<string>();
        int endIndex = startIndex;

        for (int i = startIndex; i < tokens.Count; i++)
        {
            var token = tokens[i];
            jsonParts.Add(token);
            endIndex = i;

            bool inString = false;
            bool escaped = false;
            foreach (char c in token)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }
                if (!inString)
                {
                    if (c == openChar) depth++;
                    if (c == closeChar) depth--;
                }
            }

            if (depth == 0)
                break;
        }

        var merged = string.Join("", jsonParts);

        if (openChar == '{' && !string.IsNullOrEmpty(merged))
        {
            merged = RestoreJsonObject(merged);
        }

        return (merged, endIndex);
    }

    public static string RestoreJsonObject(string json)
    {
        if (string.IsNullOrEmpty(json))
            return json;

        var inner = json.TrimStart('{').TrimEnd('}').Trim();
        if (string.IsNullOrEmpty(inner))
            return json;

        var restoredParts = new List<string>();
        int currentIndex = 0;

        while (currentIndex < inner.Length)
        {
            while (currentIndex < inner.Length && char.IsWhiteSpace(inner[currentIndex]))
                currentIndex++;

            if (currentIndex >= inner.Length)
                break;

            var colonIndex = inner.IndexOf(':', currentIndex);
            if (colonIndex < 0)
            {
                restoredParts.Add(inner.Substring(currentIndex).Trim());
                break;
            }

            var key = inner.Substring(currentIndex, colonIndex - currentIndex).Trim().Trim('"');

            currentIndex = colonIndex + 1;

            while (currentIndex < inner.Length && char.IsWhiteSpace(inner[currentIndex]))
                currentIndex++;

            if (currentIndex >= inner.Length)
            {
                restoredParts.Add($"\"{key}\":");
                break;
            }

            string value;
            if (inner[currentIndex] == '"' || inner[currentIndex] == '\'')
            {
                var quoteChar = inner[currentIndex];
                currentIndex++;
                var valueStart = currentIndex;
                while (currentIndex < inner.Length && inner[currentIndex] != quoteChar)
                {
                    if (inner[currentIndex] == '\\') currentIndex++;
                    currentIndex++;
                }
                value = inner.Substring(valueStart, currentIndex - valueStart);
                if (currentIndex < inner.Length) currentIndex++;
                value = $"\"{value}\"";
            }
            else if (inner[currentIndex] == '{' || inner[currentIndex] == '[')
            {
                var depth = 1;
                var bracketChar = inner[currentIndex];
                var closeChar = bracketChar == '{' ? '}' : ']';
                var valueStart = currentIndex;
                currentIndex++;
                while (currentIndex < inner.Length && depth > 0)
                {
                    if (inner[currentIndex] == bracketChar) depth++;
                    else if (inner[currentIndex] == closeChar) depth--;
                    if (depth > 0) currentIndex++;
                }
                value = inner.Substring(valueStart, currentIndex - valueStart + 1);
                if (currentIndex < inner.Length) currentIndex++;
            }
            else
            {
                var valueStart = currentIndex;
                while (currentIndex < inner.Length && inner[currentIndex] != ',')
                    currentIndex++;
                value = inner.Substring(valueStart, currentIndex - valueStart).Trim();

                if (!long.TryParse(value, out _) &&
                    !double.TryParse(value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out _) &&
                    !bool.TryParse(value, out _) &&
                    value != "null" &&
                    value != "true" &&
                    value != "false" &&
                    !(value.StartsWith("\"") && value.EndsWith("\"")))
                {
                    value = $"\"{value}\"";
                }
            }

            restoredParts.Add($"\"{key}\":{value}");

            if (currentIndex < inner.Length && inner[currentIndex] == ',')
                currentIndex++;
        }

        return "{" + string.Join(",", restoredParts) + "}";
    }
}
