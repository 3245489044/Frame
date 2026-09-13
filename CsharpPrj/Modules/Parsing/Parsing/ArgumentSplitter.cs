
using System.Text;

namespace Modules.Parsing.Parsing
{
    public static class ArgumentSplitter
    {
        public static string?[] SplitArguments(string rawArgs, int expectedCount = -1)
        {
            if (string.IsNullOrEmpty(rawArgs))
                return Array.Empty<string>();

            var parts = SplitWithQuotes(rawArgs, '|');

            if (expectedCount <= 0)
                return parts.ToArray();

            if (parts.Count == expectedCount)
                return parts.ToArray();

            if (parts.Count > expectedCount)
            {
                if (parts.Count < 2)
                {
                    return parts.ToArray();
                }

                var merged = new List<string?> { parts[0] };
                var extraCount = parts.Count - expectedCount;
                var firstParam = parts[1];
                for (int i = 2; i < 2 + extraCount; i++)
                    firstParam += "|" + parts[i];
                merged.Add(firstParam);
                merged.AddRange(parts.Skip(2 + extraCount));
                return merged.ToArray();
            }

            var padded = new List<string?>(parts);
            while (padded.Count < expectedCount)
                padded.Add(null);
            return padded.ToArray();
        }

        public static List<string> SplitWithQuotes(string input, char separator)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;
            bool escaped = false;
            int bracketDepth = 0;

            foreach (char c in input)
            {
                // 反斜杠转义其后一个字符：使 \" 不再翻转引号态（与 CommandTokenizer 的转义语义一致）
                if (escaped)
                {
                    current.Append(c);
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == '[' && !inQuotes)
                {
                    bracketDepth++;
                    current.Append(c);
                }
                else if (c == ']' && !inQuotes && bracketDepth > 0)
                {
                    bracketDepth--;
                    current.Append(c);
                }
                else if (c == separator && !inQuotes && bracketDepth == 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString().Trim());

            if (inQuotes && result.Count >= 2)
            {
                var secondLast = result[^2];
                var last = result[^1];
                result.RemoveRange(result.Count - 2, 2);
                result.Add(secondLast + separator + last);
            }

            if (bracketDepth > 0 && result.Count >= 2)
            {
                var secondLast = result[^2];
                var last = result[^1];
                result.RemoveRange(result.Count - 2, 2);
                result.Add(secondLast + separator + last);
            }

            return result;
        }
    }
}
