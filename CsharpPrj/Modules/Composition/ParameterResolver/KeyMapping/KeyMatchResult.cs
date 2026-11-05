
namespace ParameterResolver.KeyMapping;

public class KeyMatchResult
{
    public string SourceKey { get; set; } = "";

    public string TargetKey { get; set; } = "";

    public string MappedKey { get; set; } = "";

    public string RuleType { get; set; } = "";

    public bool IsPartialMatch { get; set; } = false;

    public List<string> MatchedLevels { get; set; } = new();

    public string AppliedRuleName { get; set; } = "";

    public static KeyMatchResult Create(string sourceKey, string targetKey, string mappedKey, string ruleType) =>
        new()
        {
            SourceKey = sourceKey,
            TargetKey = targetKey,
            MappedKey = mappedKey,
            RuleType = ruleType
        };
}

