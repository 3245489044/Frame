
using ParameterResolver.KeyMapping;
using ParameterResolver.Abstractions;

namespace ParameterResolver.Tests.KeyMapping.Matchers;

[TestClass]
public class WildcardMatcherTests
{
    private static IKeyMapper<string> CreateMapper(KeyMappingConfig config)
    {
        return new KeyMapperBuilder(config).BuildMapper<string>();
    }

    [TestMethod]
    public void KeyMappingConfig_Resolve_WildcardMatch_ShouldMapCorrectly()
    {
        var config = new KeyMapperBuilder()
            .AddRule(MappingRuleType.Pattern, "user.*", "user.*")
            .Build();

        var mapper = CreateMapper(config);
        var result = mapper.Map("user.name", "user.name", config);

        Assert.AreEqual("user.name", result);
    }

    [TestMethod]
    public void KeyMappingConfig_Resolve_WildcardPrefix_ShouldMapCorrectly()
    {
        var config = new KeyMapperBuilder()
            .AddRule(MappingRuleType.Pattern, "*.value", "mapped.*")
            .Build();

        var mapper = CreateMapper(config);
        var result = mapper.Map("key.value", "mapped.key", config);

        Assert.AreEqual("mapped.key", result);
    }

    [TestMethod]
    public void KeyMappingConfig_Resolve_WildcardSuffix_ShouldMapCorrectly()
    {
        var config = new KeyMapperBuilder()
            .AddRule(MappingRuleType.Pattern, "prefix.*", "new.*")
            .Build();

        var mapper = CreateMapper(config);
        var result = mapper.Map("prefix.suffix", "new.suffix", config);

        Assert.AreEqual("new.suffix", result);
    }

    [TestMethod]
    public void KeyMappingConfig_Resolve_StarWildcard_ShouldMatchAll()
    {
        var config = new KeyMapperBuilder()
            .AddRule(MappingRuleType.Pattern, "*", "all")
            .Build();

        var mapper = CreateMapper(config);
        var result = mapper.Map("anything", "all", config);

        Assert.AreEqual("all", result);
    }

    [TestMethod]
    public void KeyMappingConfig_Resolve_EmptyConfig_ShouldReturnOriginal()
    {
        var config = new KeyMappingConfig();
        var mapper = CreateMapper(config);
        var result = mapper.Map("someKey", "someKey", config);

        Assert.AreEqual("someKey", result);
    }

    [TestMethod]
    public void KeyMappingConfig_Resolve_MultipleRules_ShouldMatchFirst()
    {
        var config = new KeyMapperBuilder()
            .AddRule(MappingRuleType.Exact, "specific", "mapped")
            .AddRule(MappingRuleType.Pattern, "*", "catchAll")
            .Build();

        var mapper = CreateMapper(config);
        var result = mapper.Map("specific", "mapped", config);

        Assert.AreEqual("mapped", result);
    }

    [TestMethod]
    public void KeyMappingConfig_Resolve_CatchAll_ShouldMatchUnknown()
    {
        var config = new KeyMapperBuilder()
            .AddRule(MappingRuleType.Exact, "specific", "mapped")
            .AddRule(MappingRuleType.Pattern, "*", "catchAll")
            .Build();

        var mapper = CreateMapper(config);
        var result = mapper.Map("unknown", "catchAll", config);

        Assert.AreEqual("catchAll", result);
    }

    [TestMethod]
    public void KeyMappingConfig_Resolve_CaseSensitivity_ShouldBeCaseSensitive()
    {
        var config = new KeyMapperBuilder()
            .AddRule(MappingRuleType.Exact, "Key", "Mapped")
            .Build();

        var mapper = CreateMapper(config);
        var result = mapper.Map("key", "Mapped", config);

        Assert.AreEqual("Mapped", result);
    }

    [TestMethod]
    public void KeyMappingConfig_Resolve_DotSeparator_ShouldMatchCorrectly()
    {
        var config = new KeyMapperBuilder()
            .AddMultiLevelRule()
            .AddLevelPattern("a", "x")
            .AddLevelPattern("b", "y")
            .AddLevelPattern("c", "z")
            .Build();

        var mapper = CreateMapper(config);
        var result = mapper.Map("x.y.z", "a.b.c", config);

        Assert.AreEqual("x.y.z", result);
    }
}
