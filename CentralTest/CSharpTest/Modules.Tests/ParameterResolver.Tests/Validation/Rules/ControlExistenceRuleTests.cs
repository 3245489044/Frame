
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;
using ParameterResolver.Validation.Providers;
using ParameterResolver.Validation.Rules.Runtime;

namespace ParameterResolver.Validation.Tests.Rules;

[TestClass]
public class ControlExistenceRuleTests
{
    private readonly ControlExistenceRule _rule = new();

    [TestMethod]
    public async Task ValidateAsync_NoControlTreeProvider_ShouldReturnWarning()
    {
        var context = new ConfigValidationContext();
        var json = """{"controls":[{"name":"btn1"}]}""";
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(Severity.Warning, diagnostics[0].Severity);
        Assert.IsTrue(diagnostics[0].Message.Contains("控件树提供者未配置"));
    }

    [TestMethod]
    public async Task ValidateAsync_AllControlsExist_ShouldReturnNoDiagnostics()
    {
        var context = new ConfigValidationContext
        {
            ControlTree = new MockControlTreeProvider(new[] { "btn1", "panel1", "txt1" })
        };
        var json = """
        {
            "controls": [
                { "name": "btn1", "type": "button" }
            ],
            "bindings": {
                "parameters": [
                    { "target": "text", "source": "btn1" },
                    { "target": "title", "source": "panel1" },
                    { "target": "value", "source": "txt1" }
                ]
            }
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_ControlNotInTree_ShouldReturnError()
    {
        var context = new ConfigValidationContext
        {
            ControlTree = new MockControlTreeProvider(new[] { "btn1" })
        };
        var json = """
        {
            "controls": [
                { "name": "btn1", "type": "button" }
            ],
            "bindings": {
                "parameters": [
                    { "target": "text", "source": "btn1" },
                    { "target": "value", "source": "nonExistent" }
                ]
            }
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual("ControlExistence", diagnostics[0].RuleName);
        Assert.AreEqual(Severity.Error, diagnostics[0].Severity);
        Assert.IsTrue(diagnostics[0].Message.Contains("nonExistent"));
    }

    [TestMethod]
    public async Task ValidateAsync_NestedControls_ShouldValidateAllLevels()
    {
        var context = new ConfigValidationContext
        {
            ControlTree = new MockControlTreeProvider(new[] { "parent", "child1" })
        };
        var json = """
        {
            "controls": [{
                "name": "parent",
                "type": "panel",
                "children": [
                    { "name": "child1", "type": "button" },
                    { "name": "child2", "type": "textbox" }
                ]
            }],
            "bindings": {
                "parameters": [
                    { "target": "text", "source": "parent" },
                    { "target": "title", "source": "child1" },
                    { "target": "value", "source": "child2" }
                ]
            }
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.IsTrue(diagnostics[0].Message.Contains("child2"));
    }

    [TestMethod]
    public async Task ValidateAsync_EmptyControls_ShouldReturnNoDiagnostics()
    {
        var context = new ConfigValidationContext
        {
            ControlTree = new MockControlTreeProvider(Array.Empty<string>())
        };
        var json = """{"controls":[]}""";
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    private class MockControlTreeProvider : IControlTreeProvider
    {
        private readonly HashSet<string> _controls;

        public MockControlTreeProvider(string[] controls)
        {
            _controls = new HashSet<string>(controls);
        }

        public string Name => "MockControlTree";

        public IReadOnlySet<string> GetControlNames() => _controls;

        public bool HasControl(string controlName) => _controls.Contains(controlName);

        public string? GetControlType(string controlName) =>
            _controls.Contains(controlName) ? "Button" : null;

        public IReadOnlyList<string>? GetSupportedEvents(string controlName) =>
            _controls.Contains(controlName) ? new[] { "Click", "Load" } : null;
    }
}
