
using ParameterResolver.Validation;
using ParameterResolver.Validation.Providers;

namespace ParameterResolver.Validation.Tests;

[TestClass]
public class ConfigValidationContextTests
{
    [TestMethod]
    public void Constructor_ShouldInitializeEmpty()
    {
        var context = new ConfigValidationContext();

        Assert.IsNull(context.ControlTree);
        Assert.IsNull(context.FunctionRegistry);
        Assert.IsNull(context.TemplateRegistry);
    }

    [TestMethod]
    public void SetControlTree_ShouldStoreProvider()
    {
        var context = new ConfigValidationContext();
        var provider = new MockControlTreeProvider();

        context.ControlTree = provider;

        Assert.AreSame(provider, context.ControlTree);
    }

    [TestMethod]
    public void SetFunctionRegistry_ShouldStoreProvider()
    {
        var context = new ConfigValidationContext();
        var provider = new MockFunctionRegistryProvider();

        context.FunctionRegistry = provider;

        Assert.AreSame(provider, context.FunctionRegistry);
    }

    [TestMethod]
    public void SetTemplateRegistry_ShouldStoreProvider()
    {
        var context = new ConfigValidationContext();
        var provider = new MockTemplateRegistryProvider();

        context.TemplateRegistry = provider;

        Assert.AreSame(provider, context.TemplateRegistry);
    }

    [TestMethod]
    public void SetAllProviders_ShouldStoreAll()
    {
        var context = new ConfigValidationContext
        {
            ControlTree = new MockControlTreeProvider(),
            FunctionRegistry = new MockFunctionRegistryProvider(),
            TemplateRegistry = new MockTemplateRegistryProvider()
        };

        Assert.IsNotNull(context.ControlTree);
        Assert.IsNotNull(context.FunctionRegistry);
        Assert.IsNotNull(context.TemplateRegistry);
    }

    private class MockControlTreeProvider : IControlTreeProvider
    {
        public string Name => "MockControlTree";
        public IReadOnlySet<string> GetControlNames() => new HashSet<string>();
        public bool HasControl(string controlName) => true;
        public string? GetControlType(string controlName) => "Button";
        public IReadOnlyList<string>? GetSupportedEvents(string controlName) => new[] { "Click" };
    }

    private class MockFunctionRegistryProvider : IFunctionRegistryProvider
    {
        public string Name => "MockFunctionRegistry";
        public IReadOnlySet<string> GetFunctionNames() => new HashSet<string>();
        public bool HasFunction(string functionName) => true;
        public IReadOnlyList<string>? GetFunctionParameterTypes(string functionName) => new[] { "string" };
    }

    private class MockTemplateRegistryProvider : ITemplateRegistryProvider
    {
        public string Name => "MockTemplateRegistry";
        public IReadOnlySet<string> GetTemplateNames() => new HashSet<string>();
        public bool HasTemplate(string templateName) => true;
        public string? GetTemplateDescription(string templateName) => "Mock template";
    }
}
