
using ParameterResolver.KeyMapping;
using ParameterResolver.Abstractions;

namespace ParameterResolver.Tests.KeyMapping.Matchers;

[TestClass]
public class PatternMatcherTests
{
    private static IKeyMapper<string> CreateMapper(KeyMappingConfig config)
    {
        return new KeyMapperBuilder(config).BuildMapper<string>();
    }

    [TestMethod]
    public void KeyMappingConfig_Constructor_ShouldCreate()
    {
        var config = new KeyMappingConfig();

        Assert.IsNotNull(config);
    }

    [TestMethod]
    public void KeyMappingConfig_DefaultWildcard_ShouldBeStar()
    {
        var config = new KeyMappingConfig();

        Assert.AreEqual("*", config.WildcardChar);
    }

    [TestMethod]
    public void KeyMappingConfig_DefaultSeparator_ShouldBeDot()
    {
        var config = new KeyMappingConfig();

        Assert.AreEqual(".", config.DefaultSeparator);
    }

    [TestMethod]
    public void KeyMappingConfig_HierarchyNormalization_DefaultShouldBeTrue()
    {
        var config = new KeyMappingConfig();

        Assert.IsTrue(config.EnableHierarchyNormalization);
    }

    [TestMethod]
    public void KeyMappingConfig_ExactMatch_ShouldSetCorrectType()
    {
        var config = new KeyMapperBuilder()
            .AddRule(MappingRuleType.Exact, "exactKey", "mappedKey")
            .Build();

        Assert.AreEqual(1, config.Rules.Count);
        Assert.AreEqual("exactKey", config.Rules[0].SourcePattern);
        Assert.AreEqual("mappedKey", config.Rules[0].TargetPattern);
        Assert.AreEqual(MappingRuleType.Exact, config.Rules[0].RuleType);
    }

    [TestMethod]
    public void KeyMappingConfig_PatternMatch_ShouldSetCorrectType()
    {
        var config = new KeyMapperBuilder()
            .AddRule(MappingRuleType.Pattern, "prefix_*", "mapped_*")
            .Build();

        Assert.AreEqual(1, config.Rules.Count);
        Assert.AreEqual(MappingRuleType.Pattern, config.Rules[0].RuleType);
    }

    [TestMethod]
    public void KeyMappingConfig_MultiLevel_ShouldSetCorrectType()
    {
        var config = new KeyMapperBuilder()
            .AddMultiLevelRule()
            .AddLevelPattern("a", "x")
            .AddLevelPattern("b", "y")
            .Build();

        Assert.AreEqual(1, config.Rules.Count);
        Assert.AreEqual(MappingRuleType.MultiLevel, config.Rules[0].RuleType);
    }

    [TestMethod]
    public void KeyMappingConfig_AddMultipleRules_ShouldContainAll()
    {
        var config = new KeyMapperBuilder()
            .AddRule(MappingRuleType.Exact, "key1", "map1")
            .AddRule(MappingRuleType.Pattern, "key2", "map2")
            .Build();

        Assert.AreEqual(2, config.Rules.Count);
    }

    [TestMethod]
    public void KeyMappingConfig_Resolve_ExactMatch_ShouldReturnMappedKey()
    {
        var config = new KeyMapperBuilder()
            .AddRule(MappingRuleType.Exact, "oldKey", "newKey")
            .Build();

        var mapper = CreateMapper(config);
        var result = mapper.Map("oldKey", "newKey", config);

        Assert.AreEqual("newKey", result);
    }

    [TestMethod]
    public void KeyMappingConfig_Resolve_NoMatch_ShouldReturnOriginal()
    {
        var config = new KeyMapperBuilder()
            .AddRule(MappingRuleType.Exact, "oldKey", "newKey")
            .Build();

        var mapper = CreateMapper(config);
        var result = mapper.Map("unknownKey", "newKey", config);

        Assert.AreEqual("newKey", result);
    }

    [TestMethod]
    public void KeyMappingConfig_Resolve_PatternMatch_ShouldMapCorrectly()
    {
        var config = new KeyMapperBuilder()
            .AddRule(MappingRuleType.Pattern, "prefix_*", "mapped_*")
            .Build();

        var mapper = CreateMapper(config);
        var result = mapper.Map("prefix_value", "mapped_value", config);

        Assert.AreEqual("mapped_value", result);
    }

    [TestMethod]
    public void KeyMappingConfig_Resolve_MultiLevel_ShouldMapCorrectly()
    {
        var config = new KeyMapperBuilder()
            .AddMultiLevelRule()
            .AddLevelPattern("a", "x")
            .AddLevelPattern("b", "y")
            .Build();

        var mapper = CreateMapper(config);
        var result = mapper.Map("x.y", "a.b", config);

        Assert.AreEqual("x.y", result);
    }
}
