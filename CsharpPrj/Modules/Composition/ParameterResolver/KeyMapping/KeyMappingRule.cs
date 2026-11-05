
namespace ParameterResolver.KeyMapping;

public enum MappingRuleType
{
    Exact,

    Pattern,

    MultiLevel,

}

public class MappingRule
{
    public MappingRuleType RuleType { get; set; } = MappingRuleType.Exact;

    public int Priority { get; set; } = 10;

    public string LevelSeparator { get; set; } = ".";

    public string SourcePattern { get; set; } = "";

    public string TargetPattern { get; set; } = "";

    public List<LevelPattern> LevelPatterns { get; set; } = new();

    public bool RequireAllLevels { get; set; } = true;

    public bool MatchAllSourceKeys { get; set; } = false;
}

public class LevelPattern
{
    public string TargetPattern { get; set; } = "";

    public string SourcePattern { get; set; } = "";

    public bool IsRequired { get; set; } = true;
}
