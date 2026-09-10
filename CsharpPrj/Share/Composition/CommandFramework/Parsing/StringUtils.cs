
using System.Text;

namespace CommandFramework.Parsing
{
    public static class StringUtils
    {
        public static List<string> SplitWithQuotesAndEscape(string input, char separator, bool preserveQuotes = false, bool restoreUnclosedQuote = false)
        {
            if (string.IsNullOrEmpty(input))
                return new List<string>();

            var result = new List<string>();
            var current = new StringBuilder();
            var inQuote = false;
            var escaped = false;

            for (int i = 0; i < input.Length; i++)
            {
                var c = input[i];

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
                    if (!preserveQuotes)
                        inQuote = !inQuote;
                    else
                        current.Append(c);
                }
                else if (c == separator && !inQuote)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            if (inQuote && restoreUnclosedQuote && current.Length > 0)
            {
                current.Insert(0, '"');
                result.Add(current.ToString());
            }
            else if (current.Length > 0)
            {
                result.Add(current.ToString());
            }

            return result;
        }

        public static List<string> SplitEscaped(string input, char separator = ',')
        {
            return SplitWithQuotesAndEscape(input, separator);
        }

        public static string[] SplitEscapedToArray(string input, char separator = ',')
        {
            return SplitWithQuotesAndEscape(input, separator).ToArray();
        }

        public static List<string> SplitCommand(string command, bool restoreUnclosedQuote = true)
        {
            return SplitWithQuotesAndEscape(command, ' ', restoreUnclosedQuote: restoreUnclosedQuote);
        }

        public static List<string> SplitArguments(string rawArgs)
        {
            return SplitWithQuotesAndEscape(rawArgs, '|');
        }
    }
}