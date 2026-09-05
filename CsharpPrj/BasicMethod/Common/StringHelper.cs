
using System;

namespace Common
{
    public static class StringHelper
    {
        public static int FindFirstUnprotectedSeparator(
            string input,
            char separator = '|',
            (char Open, char Close)[]? protectChars = null)
        {
            if (string.IsNullOrEmpty(input))
                return -1;

            protectChars ??= new[] { ('"', '"'), ('[', ']') };

            bool inQuotes = false;
            int bracketDepth = 0;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '"')
                    inQuotes = !inQuotes;
                else if (c == '[' && !inQuotes)
                    bracketDepth++;
                else if (c == ']' && !inQuotes && bracketDepth > 0)
                    bracketDepth--;
                else if (c == separator && !inQuotes && bracketDepth == 0)
                    return i;
            }

            return -1;
        }
    }
}
