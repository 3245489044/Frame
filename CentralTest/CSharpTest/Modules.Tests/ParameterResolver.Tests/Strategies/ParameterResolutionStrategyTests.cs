
using ParameterResolver.Strategies;

namespace ParameterResolver.Tests.Strategies;

[TestClass]
public class ParameterResolutionStrategyTests
{
    [TestMethod]
    public void StrategyResolver_IsAbstract()
    {
        Assert.IsTrue(typeof(StrategyResolver<,>).IsAbstract);
    }

    [TestMethod]
    public void StrategyResolver_HasTwoTypeParameters()
    {
        var args = typeof(StrategyResolver<,>).GetGenericArguments();

        Assert.AreEqual(2, args.Length);
    }

    [TestMethod]
    public void StrategyResolver_IsPublic()
    {
        Assert.IsTrue(typeof(StrategyResolver<,>).IsPublic);
    }

    [TestMethod]
    public void StrategyResolver_IsGenericType()
    {
        Assert.IsTrue(typeof(StrategyResolver<,>).IsGenericType);
    }

    [TestMethod]
    public void StrategyResolver_CanCreateConcreteType()
    {
        var concreteType = typeof(TestStrategyResolver);
        var baseType = concreteType.BaseType;

        Assert.IsNotNull(baseType);
        Assert.IsTrue(baseType.IsGenericType);
        Assert.AreEqual(typeof(StrategyResolver<,>).Name, baseType.GetGenericTypeDefinition().Name);
    }

    [TestMethod]
    public void Resolve_WithExplicitAdapter_ShouldReturnStrategy()
    {
        var resolver = new TestStrategyResolver();

        var (strategy, hasExplicitConfig) = resolver.Resolve(new TestAdapter { Mode = "uppercase" });

        Assert.IsNotNull(strategy);
        Assert.IsTrue(hasExplicitConfig);
        Assert.AreEqual("UPPERCASE", strategy);
    }

    [TestMethod]
    public void Resolve_WithNullAdapter_ShouldReturnDefault()
    {
        var resolver = new TestStrategyResolver();

        var (strategy, hasExplicitConfig) = resolver.Resolve(null);

        Assert.IsNotNull(strategy);
        Assert.IsFalse(hasExplicitConfig);
        Assert.AreEqual("text", strategy);
    }

    [TestMethod]
    public void Resolve_WithUnknownMode_ShouldReturnDefault()
    {
        var resolver = new TestStrategyResolver();

        var (strategy, hasExplicitConfig) = resolver.Resolve(new TestAdapter { Mode = "nonexistent" });

        Assert.IsNotNull(strategy);
        Assert.IsFalse(hasExplicitConfig);
        Assert.AreEqual("text", strategy);
    }

    [TestMethod]
    public void Register_ShouldOverrideDefault()
    {
        var resolver = new TestStrategyResolver();
        resolver.Register("text", "custom-default");

        var (strategy, hasExplicitConfig) = resolver.Resolve(null);

        Assert.AreEqual("custom-default", strategy);
        Assert.IsFalse(hasExplicitConfig);
    }

    [TestMethod]
    public void Resolve_MultipleStrategies_ShouldReturnCorrect()
    {
        var resolver = new TestStrategyResolver();

        var (upper, upperExplicit) = resolver.Resolve(new TestAdapter { Mode = "uppercase" });
        var (lower, lowerExplicit) = resolver.Resolve(new TestAdapter { Mode = "lowercase" });
        var (text, textExplicit) = resolver.Resolve(new TestAdapter { Mode = "text" });

        Assert.AreEqual("UPPERCASE", upper);
        Assert.IsTrue(upperExplicit);
        Assert.AreEqual("lowercase", lower);
        Assert.IsTrue(lowerExplicit);
        Assert.AreEqual("text", text);
        Assert.IsTrue(textExplicit);
    }

    public class TestAdapter
    {
        public string Mode { get; set; } = "";
    }

    private class TestStrategyResolver : StrategyResolver<string, TestAdapter>
    {
        public TestStrategyResolver() : base("text")
        {
        }

        protected override void RegisterDefaults()
        {
            AddDefault("text", "text");
            AddDefault("uppercase", "UPPERCASE");
            AddDefault("lowercase", "lowercase");
        }

        protected override string? GetMode(TestAdapter adapter)
        {
            return string.IsNullOrEmpty(adapter.Mode) ? null : adapter.Mode;
        }
    }
}
