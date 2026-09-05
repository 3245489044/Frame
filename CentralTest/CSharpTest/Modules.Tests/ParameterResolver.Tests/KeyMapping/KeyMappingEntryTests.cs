
using ParameterResolver.KeyMapping;

namespace ParameterResolver.Tests.KeyMapping;

[TestClass]
public class KeyMappingEntryTests
{
    [TestMethod]
    public void Constructor_ShouldSetProperties()
    {
        var rule = new MappingRule
        {
            SourcePattern = "oldKey",
            TargetPattern = "newKey",
            RuleType = MappingRuleType.Exact,
            Priority = 5
        };

        Assert.AreEqual("oldKey", rule.SourcePattern);
        Assert.AreEqual("newKey", rule.TargetPattern);
        Assert.AreEqual(MappingRuleType.Exact, rule.RuleType);
        Assert.AreEqual(5, rule.Priority);
    }

    [TestMethod]
    public void DefaultRuleType_ShouldBeExact()
    {
        var rule = new MappingRule();

        Assert.AreEqual(MappingRuleType.Exact, rule.RuleType);
    }

    [TestMethod]
    public void DefaultPriority_ShouldBe10()
    {
        var rule = new MappingRule();

        Assert.AreEqual(10, rule.Priority);
    }

    [TestMethod]
    public void DefaultLevelSeparator_ShouldBeDot()
    {
        var rule = new MappingRule();

        Assert.AreEqual(".", rule.LevelSeparator);
    }

    [TestMethod]
    public void PatternRule_ShouldSetPattern()
    {
        var rule = new MappingRule
        {
            RuleType = MappingRuleType.Pattern,
            SourcePattern = "prefix_*",
            TargetPattern = "mapped_*"
        };

        Assert.AreEqual(MappingRuleType.Pattern, rule.RuleType);
        Assert.AreEqual("prefix_*", rule.SourcePattern);
        Assert.AreEqual("mapped_*", rule.TargetPattern);
    }

    [TestMethod]
    public void MultiLevelRule_ShouldHaveLevelPatterns()
    {
        var rule = new MappingRule
        {
            RuleType = MappingRuleType.MultiLevel,
            LevelPatterns = new List<LevelPattern>
            {
                new() { SourcePattern = "src.*", TargetPattern = "dst.*", IsRequired = true }
            }
        };

        Assert.AreEqual(MappingRuleType.MultiLevel, rule.RuleType);
        Assert.AreEqual(1, rule.LevelPatterns.Count);
        Assert.AreEqual("src.*", rule.LevelPatterns[0].SourcePattern);
        Assert.IsTrue(rule.LevelPatterns[0].IsRequired);
    }

    [TestMethod]
    public void MatchAllSourceKeys_DefaultShouldBeFalse()
    {
        var rule = new MappingRule();

        Assert.IsFalse(rule.MatchAllSourceKeys);
    }

    [TestMethod]
    public void FromRules_ShouldCreateConfig()
    {
        var rule = new MappingRule
        {
            SourcePattern = "key1",
            TargetPattern = "mapped1"
        };

        var config = KeyMappingConfig.FromRules(rule);

        Assert.AreEqual(1, config.Rules.Count);
        Assert.AreEqual("key1", config.Rules[0].SourcePattern);
    }
}
