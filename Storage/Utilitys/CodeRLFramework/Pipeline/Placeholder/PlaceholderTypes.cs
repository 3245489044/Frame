
#region USINGS
using System.Text.RegularExpressions;
#endregion

namespace CodeRLFramework.Pipeline.Placeholder
{
    public enum PlaceholderType
    {
        Input,

        Prev,

        Node,

        PrevStep,

        Context,

        Custom
    }

    public class PlaceholderDefinition
    {
        public PlaceholderType Type { get; set; }

        public string? Name { get; set; }

        public string RawText { get; set; } = string.Empty;

        public string? ResolvedValue { get; set; }

        public bool IsResolved { get; set; }

        public DateTime? ResolvedAt { get; set; }

        public string? ErrorMessage { get; set; }

        public static string GetPattern()
        {
            return @"@(input|prev|node|prevstep|context)(?::([a-zA-Z_][a-zA-Z0-9_]*))?";
        }

        public static List<PlaceholderDefinition> ParseAll(string text)
        {
            var placeholders = new List<PlaceholderDefinition>();
            if (string.IsNullOrEmpty(text)) return placeholders;

            var regex = new Regex(GetPattern(), RegexOptions.IgnoreCase);
            var matches = regex.Matches(text);

            foreach (Match match in matches)
            {
                var typeStr = match.Groups[1].Value.ToLower();
                var name = match.Groups[2].Success ? match.Groups[2].Value : null;

                var type = typeStr switch
                {
                    "input" => PlaceholderType.Input,
                    "prev" => PlaceholderType.Prev,
                    "node" => PlaceholderType.Node,
                    "prevstep" => PlaceholderType.PrevStep,
                    "context" => PlaceholderType.Context,
                    _ => PlaceholderType.Custom
                };

                placeholders.Add(new PlaceholderDefinition
                {
                    Type = type,
                    Name = name,
                    RawText = match.Value
                });
            }

            return placeholders;
        }

        public static bool ContainsPlaceholder(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return Regex.IsMatch(text, GetPattern(), RegexOptions.IgnoreCase);
        }

        public override string ToString()
        {
            return IsResolved
                ? $"{RawText} → {ResolvedValue}"
                : $"{RawText} (未解析)";
        }
    }

    public class PlaceholderResolveContext
    {
        public string? InputCode { get; set; }

        public string? PreviousCode { get; set; }

        public HashSet<string> GraphNodeNames { get; set; } = new();

        public List<string> NavigationPath { get; set; } = new();

        public int CurrentStepIndex { get; set; }

        public Func<string, string?>? CustomResolver { get; set; }

        public string? GetPrevStepNode()
        {
            if (CurrentStepIndex > 0 && CurrentStepIndex <= NavigationPath.Count)
                return NavigationPath[CurrentStepIndex - 1];
            return null;
        }
    }

    public class PlaceholderResolveResult
    {
        public string ResolvedText { get; set; } = string.Empty;

        public List<PlaceholderDefinition> Placeholders { get; set; } = new();

        public bool IsAllResolved => Placeholders.All(p => p.IsResolved);

        public List<PlaceholderDefinition> Unresolved =>
            Placeholders.Where(p => !p.IsResolved).ToList();

        public DateTime ResolvedAt { get; set; } = DateTime.UtcNow;

        public long ElapsedMs { get; set; }
    }
}
