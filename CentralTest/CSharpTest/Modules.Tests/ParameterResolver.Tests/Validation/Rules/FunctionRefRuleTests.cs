
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;
using ParameterResolver.Validation.Providers;
using ParameterResolver.Validation.Rules.Reference;

namespace ParameterResolver.Validation.Tests.Rules;

[TestClass]
public class FunctionRefRuleTests
{
    private readonly FunctionRefRule _rule = new();

    [TestMethod]
    public async Task ValidateAsync_NoFunctionRegistry_ShouldReturnInfo()
    {
        var context = new ConfigValidationContext();
        var json = """{"controls":[{"name":"btn1"}]}""";
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(Severity.Info, diagnostics[0].Severity);
        Assert.IsTrue(diagnostics[0].Message.Contains("函数注册表提供者未配置"));
    }

    [TestMethod]
    public async Task ValidateAsync_ValidFunctionRef_ShouldReturnNoDiagnostics()
    {
        var context = new ConfigValidationContext
        {
            FunctionRegistry = new MockFunctionRegistryProvider(new[] { "OnClick", "OnLoad" })
        };
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["OnClick"],
                    "Load": ["OnLoad"]
                }
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_InvalidFunctionRef_ShouldReturnError()
    {
        var context = new ConfigValidationContext
        {
            FunctionRegistry = new MockFunctionRegistryProvider(new[] { "OnClick" })
        };
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["NonExistentFunc"]
                }
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual("FunctionRef", diagnostics[0].RuleName);
        Assert.AreEqual(Severity.Error, diagnostics[0].Severity);
        Assert.IsTrue(diagnostics[0].Message.Contains("NonExistentFunc"));
    }

    [TestMethod]
    public async Task ValidateAsync_MultipleEvents_ShouldValidateAll()
    {
        var context = new ConfigValidationContext
        {
            FunctionRegistry = new MockFunctionRegistryProvider(new[] { "OnClick" })
        };
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["OnClick"],
                    "DoubleClick": ["OnDoubleClick", "OnClick"],
                    "MouseEnter": ["NonExistent"]
                }
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(2, diagnostics.Count);
        Assert.IsTrue(diagnostics.All(d => d.RuleName == "FunctionRef"));
    }

    [TestMethod]
    public async Task ValidateAsync_NoEvents_ShouldReturnNoDiagnostics()
    {
        var context = new ConfigValidationContext
        {
            FunctionRegistry = new MockFunctionRegistryProvider(new[] { "OnClick" })
        };
        var json = """{"controls":[{"name":"btn1","type":"button"}]}""";
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    private class MockFunctionRegistryProvider : IFunctionRegistryProvider
    {
        private readonly HashSet<string> _functions;

        public MockFunctionRegistryProvider(string[] functions)
        {
            _functions = new HashSet<string>(functions);
        }

        public string Name => "MockFunctionRegistry";

        public IReadOnlySet<string> GetFunctionNames() => _functions;

        public bool HasFunction(string functionName) => _functions.Contains(functionName);

        public IReadOnlyList<string>? GetFunctionParameterTypes(string functionName) =>
            _functions.Contains(functionName) ? new[] { "string", "int" } : null;
    }
}
