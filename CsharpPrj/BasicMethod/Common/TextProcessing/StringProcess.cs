namespace Common.GraphMindStudio
{
#nullable disable
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Text.Json;
    using HtmlAgilityPack;
    using System.Globalization;

    public static class StringProcess
    {
        public static class CharacterTools
        {
            public static string StringBuilderact(Action<object, object> action, int indent = 0)
            {
                var sb = new StringBuilder();
                action(sb, indent);
                return sb.ToString();
            }

            public static List<string> InputType = new() { "int", "float", "string", "double" };

            public static string StandardizeCode(string input)
            {
                string pattern = @"```csharp(.*?)```";
                MatchCollection matches = Regex.Matches(input, pattern, RegexOptions.Singleline);

                if (matches.Count == 0)
                {
                    return input;
                }

                string result = string.Join("", matches.Cast<Match>().Select(m => m.Groups[1].Value));
                return result;
            }

            public static string EscapeStringForCSharp(string input)
            {
                if (string.IsNullOrEmpty(input)) return string.Empty;

                return input.Replace("\"", "\\\"")
                           .Replace("\n", "\\n")
                           .Replace("\r", "\\r")
                           .Replace("\t", "\\t");
            }

            public static string ExtractString(object value)
            {
                if (value == null)
                    return string.Empty;

                if (value is string str)
                    return str;

                return value.ToString() ?? string.Empty;
            }

            public static string RemoveAllWhitespaces(string input)
            {
                if (input == null) return null;
                return new string(input.Where(c => !char.IsWhiteSpace(c)).ToArray());
            }

            public static string RemoveNewLines(string input)
            {
                if (string.IsNullOrEmpty(input)) return input;
                return input.Replace("\r\n", "")
                            .Replace("\n", "")
                            .Replace("\r", "");
            }

            public static string RemoveComments(string code)
            {
                if (string.IsNullOrEmpty(code))
                {
                    return string.Empty;
                }

                string pattern = @"(?<!\""')//.*?$";
                string replacedCode = Regex.Replace(code, pattern, string.Empty,
                    RegexOptions.Multiline);

                return replacedCode;
            }

            public static string RemoveSymbols(string input)
            {
                if (string.IsNullOrEmpty(input))
                    return input;
                return new string(input.Where(c => char.IsLetterOrDigit(c)).ToArray());
            }

            public static bool IsAllAllowedWhitespaces(string input)
            {
                HashSet<char> allowedWhitespaces = new()
                    {
                ' ',
                '\t',
                '\n',
                '\r',
                '\v',
                '\f',
                '\u00A0'
                };

                foreach (char c in input)
                {
                    if (!char.IsWhiteSpace(c) || !allowedWhitespaces.Contains(c))
                    {
                        return false;
                    }
                }
                return true;
            }

            public static string EscapeAngleBrackets(string text)
            {
                if (string.IsNullOrEmpty(text))
                    return text;

                return text
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;");
            }

            public static List<string> SplitExcludingBrackets(string input, char separator, char skipStart = '(', char skipEnd = ')', bool keepSeparator = false)
            {
                return SplitExcludingBracketsCore(input, separator.ToString(), skipStart, skipEnd, keepSeparator);
            }

            public static List<string> SplitBySemicolonIgnoreBraces(string input)
            {
                return SplitExcludingBrackets(input, ';', '{', '}');
            }

            public static List<string> SplitExcludingBrackets(string input, string separator, char skipStart = '(', char skipEnd = ')', bool keepSeparator = false)
            {
                return SplitExcludingBracketsCore(input, separator, skipStart, skipEnd, keepSeparator);
            }

            public static List<string> RemoveSplit(string input, char split, char removestart = '(', char removeend = ')')
            {
                return SplitExcludingBrackets(RemoveOutermostParentheses(input, removestart, removeend), split);
            }

            public static string RemoveContains(string input, string contains, char start = '(', char end = ')')
            {
                return RemoveOutermostParentheses(ContainsProcess(input, contains), start, end);
            }

            private static List<string> SplitExcludingBracketsCore(string input, string separator, char skipStart, char skipEnd, bool keepSeparator = false)
            {
                int skipDepth = 0;
                bool inQuotes = false;
                var current = new StringBuilder();
                var result = new List<string>();
                int i = 0;
                int inputLen = input.Length;
                int sepLen = separator.Length;

                while (i < inputLen)
                {
                    char c = input[i]; string addstr = c.ToString(); bool shouldAppend = true;

                    if (ProcessQuoteAndBracketChar(c, skipStart, skipEnd, ref inQuotes, ref skipDepth))
                    {
                        i++;
                    }
                    else if (!inQuotes && skipDepth == 0)
                    {
                        int sepIdx = FindNextSeparator(input, i, separator, skipStart, skipEnd);
                        if (sepIdx == i)
                        {
                            result.Add(current.ToString());
                            current.Clear();
                            if (keepSeparator)
                                current.Append(separator);
                            i += sepLen;
                            shouldAppend = false;
                        }
                        else
                        {
                            i++;
                        }
                    }
                    else
                    {
                        i++;
                    }

                    if (shouldAppend)
                        current.Append(addstr);
                }

                if (current.Length > 0 || input.Length == 0)
                {
                    result.Add(current.ToString());
                }

                return result;
            }

            public static string RemovePrefix(string source, string prefix)
            {
                if (source != null && prefix != null && source.StartsWith(prefix))
                {
                    return source.Substring(prefix.Length);
                }
                return source;
            }

            public static string RemoveExcludingBrackets(string input, string target, char skipStart = '(', char skipEnd = ')')
            {
                int skipDepth = 0;
                int currentMatchIndex = 0;
                var result = new StringBuilder();
                bool inQuotes = false;

                foreach (char c in input)
                {
                    if (ProcessQuoteAndBracketChar(c, skipStart, skipEnd, ref inQuotes, ref skipDepth))
                    {
                        result.Append(c);
                        continue;
                    }

                    if (inQuotes || skipDepth > 0)
                    {
                        result.Append(c);
                        continue;
                    }

                    if (currentMatchIndex > 0)
                    {
                        if (currentMatchIndex < target.Length &&
                            c == target[currentMatchIndex] &&
                            skipDepth == 0)
                        {
                            currentMatchIndex++;
                        }
                        else
                        {
                            if (currentMatchIndex > 0)
                            {
                                result.Append(target, 0, currentMatchIndex);
                            }
                            result.Append(c);
                            currentMatchIndex = 0;
                        }

                        if (currentMatchIndex == target.Length)
                        {
                            currentMatchIndex = 0;
                        }
                    }
                    else
                    {
                        if (c == target[0] && skipDepth == 0)
                        {
                            currentMatchIndex = 1;
                        }
                        else
                        {
                            result.Append(c);
                        }
                    }
                }

                if (currentMatchIndex > 0)
                {
                    result.Append(target, 0, currentMatchIndex);
                }

                return result.ToString();
            }

            public static string RemoveOutermostParentheses(string input, char skipStart = '(', char skipEnd = ')')
            {
                if (string.IsNullOrEmpty(input))
                    return input;

                input = input.Trim();
                if (input.Length >= 2 && input[0] == '"' && input[^1] == '"')
                    return input;

                if (input[0] != skipStart)
                    return input;

                bool inQuotes = false;
                int skipDepth = 1;
                int endIndex = -1;
                for (int i = 1; i < input.Length; i++)
                {
                    char c = input[i];
                    if (ProcessQuoteAndBracketChar(c, skipStart, skipEnd, ref inQuotes, ref skipDepth))
                    {
                        if (!inQuotes && skipDepth == 0)
                        {
                            endIndex = i;
                            break;
                        }
                        continue;
                    }
                }

                if (endIndex != input.Length - 1)
                    return input;

                return input.Substring(1, input.Length - 2).Trim();
            }

            public static string RemoveInsideSymbols(string input, char skipStart = '(', char skipEnd = ')')
            {
                if (string.IsNullOrEmpty(input)) return input;
                var sb = new StringBuilder();
                int depth = 0;
                foreach (char c in input)
                {
                    if (c == skipStart)
                    {
                        depth++;
                        continue;
                    }
                    if (c == skipEnd && depth > 0)
                    {
                        depth--;
                        continue;
                    }
                    if (depth == 0)
                    {
                        sb.Append(c);
                    }
                }
                return sb.ToString();
            }

            public static int FindNextSeparator(string input, int start, string separator, char skipStart, char skipEnd, bool skipQuotes = true)
            {
                int depth = 0;
                bool inQuotes = false;
                int sepLen = separator.Length;
                for (int i = start; i <= input.Length - sepLen; i++)
                {
                    char c = input[i];
                    if (skipQuotes && c == '"' && (i == 0 || input[i - 1] != '\\'))
                        inQuotes = !inQuotes;
                    if (!inQuotes)
                    {
                        if (c == skipStart) depth++;
                        else if (c == skipEnd && depth > 0) depth--;
                        if (depth == 0 && input.Substring(i, sepLen) == separator)
                            return i;
                    }
                }
                return -1;
            }

            private static bool ProcessQuoteAndBracketChar(
              char c,
              char skipStart,
              char skipEnd,
              ref bool inQuotes,
              ref int skipDepth)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    return true;
                }
                else if (c == skipStart)
                {
                    skipDepth++;
                    return true;
                }
                else if (c == skipEnd)
                {
                    if (skipDepth > 0)
                        skipDepth--;
                    return true;
                }
                return false;
            }

            public static string RemoveOutermostQuotes(string input)
            {
                if (string.IsNullOrEmpty(input))
                    return input;

                input = input.Trim();
                if (input.Length < 2 || input[0] != '"' || input[^1] != '"')
                    return input;

                bool startEscaped = false;
                int backslashCount = 0;
                for (int i = 0; i < input.Length && input[i] == '\\'; i++)
                    backslashCount++;
                startEscaped = (backslashCount % 2 == 1);

                if (startEscaped)
                    return input;

                backslashCount = 0;
                for (int i = input.Length - 2; i >= 0 && input[i] == '\\'; i--)
                    backslashCount++;
                bool endEscaped = (backslashCount % 2 == 1);

                if (endEscaped)
                    return input;

                return input.Substring(1, input.Length - 2);
            }

            public static string ContainsProcess(string match, string input, bool removeMatched = true, bool skipQuoted = false, bool debug = false)
            {
                if (!skipQuoted || !match.Contains("\""))
                {
                    if (match.Contains(input))
                    {
                        if (removeMatched)
                        {
                            return RemoveExcludingBrackets(match, input).Replace(" ", "");
                        }
                        else
                        {
                            return match.Replace(" ", "");
                        }
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    var sb = new StringBuilder();
                    var outsideSb = new StringBuilder();
                    bool inQuote = false;
                    int i = 0;
                    while (i < match.Length)
                    {
                        if (match[i] == '"')
                        {
                            inQuote = !inQuote;
                            sb.Append(match[i]);
                            i++;
                            continue;
                        }

                        if (inQuote)
                        {
                            sb.Append(match[i]);
                            i++;
                        }
                        else
                        {
                            outsideSb.Append(match[i]);
                            i++;
                        }
                    }

                    string outside = outsideSb.ToString();
                    string processedOutside = removeMatched
                        ? RemoveExcludingBrackets(outside, input)
                        : outside;

                    var finalSb = new StringBuilder();
                    inQuote = false;
                    int outsideIdx = 0;
                    i = 0;
                    while (i < match.Length)
                    {
                        if (match[i] == '"')
                        {
                            inQuote = !inQuote;
                            finalSb.Append(match[i]);
                            i++;
                            continue;
                        }
                        if (inQuote)
                        {
                            finalSb.Append(match[i]);
                            i++;
                        }
                        else
                        {
                            if (outsideIdx < processedOutside.Length)
                            {
                                finalSb.Append(processedOutside[outsideIdx]);
                                outsideIdx++;
                            }
                            i++;
                        }
                    }

                    string result = finalSb.ToString();

                    if (!removeMatched && result.Replace(" ", "").Contains(input))
                        return result.Replace(" ", "");
                    if (removeMatched && result.Replace(" ", "") != match.Replace(" ", ""))
                        return result.Replace(" ", "");
                    return null;
                }
            }

            public static int ContainsMutiple(string input, IEnumerable<string> matches, bool skipQuoted = false, char skipStart = '(', char skipEnd = ')', bool debug = false)
            {
                if (matches == null) throw new ArgumentNullException(nameof(matches));
                if (input == null) throw new ArgumentNullException(nameof(input));

                string searchInput = RemoveInsideSymbols(input, skipStart, skipEnd);

                if (skipQuoted)
                    searchInput = ExtractOutsideQuotes(searchInput);

                int totalCount = 0;
                foreach (var match in matches)
                {
                    if (string.IsNullOrEmpty(match)) continue;
                    int count = CountOccurrences(searchInput, match);
                    totalCount += count;
                }
                return totalCount;
            }

            public static bool ContainsBool(string match, string input, bool skipQuoted = false, bool debug = false)
            {
                if (!skipQuoted)
                {
                    return match.Contains(input);
                }
                else
                {
                    return ContainsOutsideQuotes(match, input);
                }
            }

            public static bool ContainsPattern(string input, string pattern)
            {
                Regex regex = new(pattern, RegexOptions.Compiled);
                return regex.IsMatch(input);
            }

            private static string ExtractOutsideQuotes(string input)
            {
                var sb = new StringBuilder();
                bool inQuote = false;
                for (int i = 0; i < input.Length; i++)
                {
                    if (input[i] == '"')
                    {
                        inQuote = !inQuote;
                        continue;
                    }
                    if (!inQuote)
                    {
                        sb.Append(input[i]);
                    }
                }
                return sb.ToString();
            }

            private static int CountOccurrences(string source, string target)
            {
                int count = 0, idx = 0;
                while ((idx = source.IndexOf(target, idx, StringComparison.Ordinal)) != -1)
                {
                    count++;
                    idx += target.Length;
                }
                return count;
            }

            private static bool ContainsOutsideQuotes(string source, string target)
            {
                bool inQuote = false;
                for (int i = 0; i <= source.Length - target.Length; i++)
                {
                    if (source[i] == '"')
                    {
                        inQuote = !inQuote;
                        continue;
                    }
                    if (!inQuote && source.Substring(i, target.Length) == target)
                    {
                        return true;
                    }
                }
                return false;
            }

            public static List<(string key, string value)> SpliteKeyValue(string input)
            {
                List<(string key, string value)> groups = new();

                foreach (string segment in SplitExcludingBrackets(input, ';', '{', '}'))
                {
                    int colonIndex = segment.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        string key = segment.Substring(0, colonIndex);
                        string value = segment.Substring(colonIndex + 1);
                        groups.Add(new(key, RemoveOutermostParentheses(value, '{', '}')));
                    }
                }
                return groups;
            }

            public static void BuildCellRun(string input, Action<int, List<string>> action)
            {
                List<string> BuildCell = SplitExcludingBrackets(input: input, ';', '{', '}');
                for (int i = 0; i < BuildCell.Count; i++)
                {
                    action(i, BuildCell);
                }
            }

            public static string BuildFuncParameterString(List<string> funcNames, List<string> parameters)
            {
                if (funcNames == null || parameters == null)
                    return string.Empty;

                var sb = new StringBuilder();
                int count = Math.Max(funcNames.Count, parameters.Count);

                for (int i = 0; i < count; i++)
                {
                    string func = "";
                    string param = "";
                    string build = "";
                    if (funcNames[i] != null)
                    {
                        func = i < funcNames.Count ? funcNames[i] : "";
                        build += $"FuncName:{func}";
                    }

                    if (parameters[i] != null)
                    {
                        param = i < parameters.Count ? parameters[i] : "";
                        build += $",Parameter:{param};\n";
                    }
                    else build += ";\n";

                    sb.Append(build);
                }
                return sb.ToString();
            }

            public static string ReplaceLoopCount(string input, object count)
            {
                return input.Replace("{LoopCount}", count.ToString());
            }

            public static string ReplaceBracesParameters(string input, Dictionary<string, string> parameters)
            {
                if (string.IsNullOrEmpty(input) || parameters == null || parameters.Count == 0)
                    return input;

                return Regex.Replace(input, @"\{([^{}]+)\}", match =>
                {
                    var key = match.Groups[1].Value;
                    return parameters.TryGetValue(key, out var value) ? value : match.Value;
                });
            }

            public static string ExtractContent(string input, string prefix, string endChars = "\n。")
            {
                if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(prefix)) return null;
                string pattern = $"{Regex.Escape(prefix)}([^{Regex.Escape(endChars)}]+)";
                var match = Regex.Match(input, pattern);
                return match.Success ? match.Groups[1].Value.Trim() : null;
            }

            public static Dictionary<string, object> ParseKeyValuePairs(string input)
            {
                List<string> pairs = SplitExcludingBrackets(input, ';', '(', ')');
                var kvs = new Dictionary<string, object>();
                foreach (var pair in pairs)
                {
                    if (string.IsNullOrWhiteSpace(pair)) continue;
                    var kv = SplitExcludingBrackets(pair.Trim(), ',', '(', ')');
                    if (kv.Count == 2)
                    {
                        var key = kv[0].Trim();
                        var value = kv[1].Trim();
                        if (!string.IsNullOrEmpty(key))
                            kvs[key] = value;
                    }
                    else
                    {
                        var kv2 = SplitExcludingBrackets(pair.Trim(), ':', '(', ')');
                        if (kv2.Count == 2)
                        {
                            var key = kv2[0].Trim();
                            var value = kv2[1].Trim();
                            if (!string.IsNullOrEmpty(key))
                                kvs[key] = value;
                        }
                    }
                }
                return kvs;
            }
        }

        public static class RecordTools
        {
            public static string GetCurrentTimestamp()
            {
                return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            }

            public static List<string> ExtractParts(List<string> inputList, char separator = ',', int partIndex = 0)
            {
                var results = new List<string>();
                foreach (var item in inputList)
                {
                    var parts = item.Split(separator);
                    if (parts.Length > partIndex)
                    {
                        var part = parts[partIndex].Trim();
                        results.Add(part);
                    }
                    else
                    {
                        results.Add(string.Empty);
                    }
                }
                return results;
            }

            public static List<int> GetDelayMillisecondsList(List<string> times)
            {
                if (times == null || times.Count < 2) return new List<int>();
                var result = new List<int>();
                for (int i = 1; i < times.Count; i++)
                {
                    var prev = DateTime.ParseExact(times[i - 1], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                    var curr = DateTime.ParseExact(times[i], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                    result.Add((int)(curr - prev).TotalMilliseconds);
                }
                return result;
            }
        }

        public class RichTextGenerator
        {
            private StringBuilder richTextBuilder;
            public RichTextGenerator()
            {
                richTextBuilder = new StringBuilder();
            }

            public RichTextGenerator AddColoredText(string text, string color)
            {
                richTextBuilder.Append($"<color={color}>{text}</color>");
                return this;
            }

            public static string GetPlainText(string richText)
            {
                var htmlDoc = new HtmlDocument();

                try
                {
                    htmlDoc.LoadHtml(richText);
                }
                catch (Exception ex)
                {
                    return $"Error parsing HTML: {ex.Message}";
                }

                var sb = new StringBuilder();

                void TraverseNodes(HtmlNode node)
                {
                    foreach (var child in node.ChildNodes)
                    {
                        if (child.NodeType == HtmlNodeType.Text ||
                            child.NodeType == HtmlNodeType.Comment)
                        {
                            sb.Append(child.InnerText);
                        }
                        else if (child.NodeType == HtmlNodeType.Element)
                        {
                            TraverseNodes(child);
                        }
                    }
                }

                TraverseNodes(htmlDoc.DocumentNode);

                return sb.ToString().Trim();
            }

            public override string ToString()
            {
                return richTextBuilder.ToString();
            }

            public static string HighlightTextWithColors(string longText, Dictionary<string, string> searchColorMap, string defaultColor = "default")
            {
                if (string.IsNullOrEmpty(longText) || searchColorMap == null || searchColorMap.Count == 0)
                {
                    return longText;
                }

                RichTextGenerator generator = new();
                int index = 0;

                while (index < longText.Length)
                {
                    bool matched = false;

                    foreach (var kvp in searchColorMap)
                    {
                        string searchText = kvp.Key;
                        string color = kvp.Value;

                        int foundIndex = longText.IndexOf(searchText, index);
                        if (foundIndex != -1)
                        {
                            if (foundIndex > index)
                            {
                                generator.AddColoredText(longText.Substring(index, foundIndex - index), defaultColor);
                            }

                            generator.AddColoredText(searchText, color);

                            index = foundIndex + searchText.Length;
                            matched = true;
                            break;
                        }
                    }

                    if (!matched)
                    {
                        generator.AddColoredText(longText.Substring(index), defaultColor);
                        break;
                    }
                }

                return generator.ToString();
            }

            public static string RGBToHex(int r, int g, int b)
            {
                r = Math.Clamp(r, 0, 255);
                g = Math.Clamp(g, 0, 255);
                b = Math.Clamp(b, 0, 255);

                return $"#{r:X2}{g:X2}{b:X2}";
            }
        }

        public static class JsonToReadableConverter
        {
            public static string Convert(string json)
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(json);
                    var result = new StringBuilder();
                    FormatElement(document.RootElement, 0, result);
                    return result.ToString();
                }
                catch (Exception ex)
                {
                    return $"解析 JSON 时出错：{ex.Message}";
                }
            }

            private static void FormatElement(JsonElement element, int indentLevel, StringBuilder sb)
            {
                string indent = new string(' ', indentLevel * 2);

                switch (element.ValueKind)
                {
                    case JsonValueKind.Object:
                        foreach (var property in element.EnumerateObject())
                        {
                            sb.Append(indent);
                            sb.Append(property.Name);
                            sb.Append(": ");
                            sb.AppendLine();
                            FormatElement(property.Value, indentLevel + 1, sb);
                        }
                        break;

                    case JsonValueKind.Array:
                        int index = 0;
                        foreach (var item in element.EnumerateArray())
                        {
                            sb.Append(indent);
                            sb.Append("- [");
                            sb.Append(index++);
                            sb.Append("]: ");
                            sb.AppendLine();
                            FormatElement(item, indentLevel + 1, sb);
                        }
                        break;

                    case JsonValueKind.String:
                        string str = element.GetString();
                        var lines = str.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                        foreach (var line in lines)
                        {
                            sb.Append(indent);
                            sb.AppendLine(line);
                        }
                        break;

                    case JsonValueKind.Number:
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                    case JsonValueKind.Null:
                        sb.Append(indent);
                        sb.AppendLine(element.ToString());
                        break;

                    default:
                        sb.Append(indent);
                        sb.AppendLine(element.ToString());
                        break;
                }
            }
        }
    }
}