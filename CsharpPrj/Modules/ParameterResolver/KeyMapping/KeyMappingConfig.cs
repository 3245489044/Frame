
namespace ParameterResolver.KeyMapping;

public class KeyMappingConfig
{
    public List<MappingRule> Rules { get; set; } = new();

    public string WildcardChar { get; set; } = "*";

    public bool EnableHierarchyNormalization { get; set; } = true;

    public string DefaultSeparator { get; set; } = ".";

    public static KeyMappingConfig FromRules(params MappingRule[] rules) =>
        new() { Rules = rules.ToList() };
}
