
using ParameterResolver.Abstractions;

namespace ParameterResolver.KeyMapping;

public class KeyMapperBuilder
{
    private readonly KeyMappingConfig _config;

    public KeyMapperBuilder()
    {
        _config = new KeyMappingConfig();
    }

    public KeyMapperBuilder(KeyMappingConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public KeyMapperBuilder WithWildcard(string wildcardChar)
    {
        _config.WildcardChar = wildcardChar;
        return this;
    }

    public KeyMapperBuilder WithSeparator(string separator)
    {
        _config.DefaultSeparator = separator;
        return this;
    }

    public KeyMapperBuilder WithNormalization(bool enabled)
    {
        _config.EnableHierarchyNormalization = enabled;
        return this;
    }

    public KeyMapperBuilder AddRule(MappingRuleType ruleType, string sourcePattern, string targetPattern, int priority = 10)
    {
        _config.Rules.Add(new MappingRule
        {
            RuleType = ruleType,
            SourcePattern = sourcePattern,
            TargetPattern = targetPattern,
            Priority = priority
        });
        return this;
    }

    public KeyMapperBuilder AddExactRule(string sourcePattern, string targetPattern, int priority = 10)
        => AddRule(MappingRuleType.Exact, sourcePattern, targetPattern, priority);

    public KeyMapperBuilder AddPatternRule(string sourcePattern, string targetPattern, int priority = 10)
        => AddRule(MappingRuleType.Pattern, sourcePattern, targetPattern, priority);

    public KeyMapperBuilder AddMultiLevelRule(string levelSeparator = ".", int priority = 10, bool requireAllLevels = true)
    {
        _config.Rules.Add(new MappingRule
        {
            RuleType = MappingRuleType.MultiLevel,
            LevelSeparator = levelSeparator,
            Priority = priority,
            RequireAllLevels = requireAllLevels
        });
        return this;
    }

    public KeyMapperBuilder AddLevelPattern(string targetPattern, string sourcePattern, bool isRequired = true)
    {
        var lastRule = _config.Rules.LastOrDefault();
        if (lastRule == null)
            throw new InvalidOperationException("必须先添加规则，再添加层级模式");

        lastRule.LevelPatterns.Add(new LevelPattern
        {
            TargetPattern = targetPattern,
            SourcePattern = sourcePattern,
            IsRequired = isRequired
        });
        return this;
    }

    public KeyMapperBuilder MatchAllSourceKeys(bool matchAll = true)
    {
        var lastRule = _config.Rules.LastOrDefault();
        if (lastRule != null)
            lastRule.MatchAllSourceKeys = matchAll;
        return this;
    }

    public KeyMappingConfig Build() => _config;

    public IKeyMapper<TKey> BuildMapper<TKey>(KeyMapperBase<TKey>? mapper = null)
        where TKey : notnull
    {
        if (mapper != null)
            return mapper;

        if (typeof(TKey) == typeof(string))
            return (IKeyMapper<TKey>)(object)new StringKeyMapper();

        throw new NotSupportedException($"No default mapper for type {typeof(TKey).Name}. Provide a custom mapper.");
    }

    public class StringKeyMapper : KeyMapperBase<string>
    {
        protected override string TrimKey(string key) => key?.Trim() ?? string.Empty;

        protected override bool IsEmptyKey(string key) => string.IsNullOrWhiteSpace(key);
    }
}
