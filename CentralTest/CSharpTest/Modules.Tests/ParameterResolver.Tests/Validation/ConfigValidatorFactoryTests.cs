
using ParameterResolver.Validation;

namespace ParameterResolver.Validation.Tests;

[TestClass]
public class ConfigValidatorFactoryTests
{
    [TestMethod]
    public void CreateDefaultEngine_ShouldReturnEngine()
    {
        var engine = ConfigValidatorFactory.CreateDefaultEngine();

        Assert.IsNotNull(engine);
        Assert.IsInstanceOfType(engine, typeof(ValidationEngine));
    }

    [TestMethod]
    public void CreateDefaultEngine_ShouldHaveRules()
    {
        var engine = ConfigValidatorFactory.CreateDefaultEngine();

        Assert.IsTrue(engine.RuleCount > 0);
    }

    [TestMethod]
    public void CreateMinimalEngine_ShouldReturnEngine()
    {
        var engine = ConfigValidatorFactory.CreateMinimalEngine();

        Assert.IsNotNull(engine);
        Assert.IsTrue(engine.RuleCount > 0);
    }

    [TestMethod]
    public void CreateReferenceEngine_ShouldReturnEngine()
    {
        var engine = ConfigValidatorFactory.CreateReferenceEngine();

        Assert.IsNotNull(engine);
        Assert.IsTrue(engine.RuleCount > 0);
    }

    [TestMethod]
    public void CreateSemanticEngine_ShouldReturnEngine()
    {
        var engine = ConfigValidatorFactory.CreateSemanticEngine();

        Assert.IsNotNull(engine);
        Assert.IsTrue(engine.RuleCount > 0);
    }
}
