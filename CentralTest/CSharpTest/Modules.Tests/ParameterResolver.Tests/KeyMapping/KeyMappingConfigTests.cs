
using ParameterResolver.KeyMapping;

namespace ParameterResolver.Tests.KeyMapping;

[TestClass]
public class KeyMappingConfigTests
{
    [TestMethod]
    public void Constructor_ShouldInitializeEmpty()
    {
        var config = new KeyMappingConfig();

        Assert.IsNotNull(config.Rules);
        Assert.AreEqual(0, config.Rules.Count);
    }

    [TestMethod]
    public void DefaultWildcard_ShouldBeStar()
    {
        var config = new KeyMappingConfig();

        Assert.AreEqual("*", config.WildcardChar);
    }

    [TestMethod]
    public void DefaultSeparator_ShouldBeDot()
    {
        var config = new KeyMappingConfig();

        Assert.AreEqual(".", config.DefaultSeparator);
    }

    [TestMethod]
    public void DefaultEnableHierarchyNormalization_ShouldBeTrue()
    {
        var config = new KeyMappingConfig();

        Assert.IsTrue(config.EnableHierarchyNormalization);
    }

    [TestMethod]
    public void FromRules_SingleRule_ShouldCreateConfig()
    {
        var config = KeyMappingConfig.FromRules(
            new MappingRule { SourcePattern = "oldKey", TargetPattern = "newKey" });

        Assert.AreEqual(1, config.Rules.Count);
        Assert.AreEqual("oldKey", config.Rules[0].SourcePattern);
        Assert.AreEqual("newKey", config.Rules[0].TargetPattern);
    }

    [TestMethod]
    public void FromRules_MultipleRules_ShouldCreateConfig()
    {
        var config = KeyMappingConfig.FromRules(
            new MappingRule { SourcePattern = "k1", TargetPattern = "v1" },
            new MappingRule { SourcePattern = "k2", TargetPattern = "v2" },
            new MappingRule { SourcePattern = "k3", TargetPattern = "v3" });

        Assert.AreEqual(3, config.Rules.Count);
    }

    [TestMethod]
    public void FromRules_NoRules_ShouldCreateEmptyConfig()
    {
        var config = KeyMappingConfig.FromRules();

        Assert.AreEqual(0, config.Rules.Count);
    }

    [TestMethod]
    public void Rules_AddAndAccess_ShouldWork()
    {
        var config = new KeyMappingConfig();
        config.Rules.Add(new MappingRule { SourcePattern = "key1", TargetPattern = "mapped1" });
        config.Rules.Add(new MappingRule { SourcePattern = "key2", TargetPattern = "mapped2" });

        Assert.AreEqual(2, config.Rules.Count);
        Assert.AreEqual("key1", config.Rules[0].SourcePattern);
        Assert.AreEqual("mapped2", config.Rules[1].TargetPattern);
    }

    [TestMethod]
    public void Rules_Remove_ShouldWork()
    {
        var config = new KeyMappingConfig();
        config.Rules.Add(new MappingRule { SourcePattern = "key1", TargetPattern = "mapped1" });

        config.Rules.RemoveAt(0);

        Assert.AreEqual(0, config.Rules.Count);
    }

    [TestMethod]
    public void Rules_Clear_ShouldWork()
    {
        var config = new KeyMappingConfig();
        config.Rules.Add(new MappingRule { SourcePattern = "k1", TargetPattern = "v1" });
        config.Rules.Add(new MappingRule { SourcePattern = "k2", TargetPattern = "v2" });

        config.Rules.Clear();

        Assert.AreEqual(0, config.Rules.Count);
    }
}
