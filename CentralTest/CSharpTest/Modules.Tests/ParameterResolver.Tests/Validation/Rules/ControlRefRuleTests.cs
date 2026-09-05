
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;
using ParameterResolver.Validation.Providers;
using ParameterResolver.Validation.Rules.Reference;

namespace ParameterResolver.Validation.Tests.Rules;

[TestClass]
public class ControlRefRuleTests
{
    private readonly ControlRefRule _rule = new();

    [TestMethod]
    public async Task ValidateAsync_NoControlTreeProvider_ShouldReturnInfo()
    {
        var context = new ConfigValidationContext();
        var json = """{"controls":[{"name":"btn1"}]}""";
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(Severity.Info, diagnostics[0].Severity);
        Assert.IsTrue(diagnostics[0].Message.Contains("控件树提供者未配置"));
    }

    [TestMethod]
    public async Task ValidateAsync_ValidSourceRef_ShouldReturnNoDiagnostics()
    {
        var context = new ConfigValidationContext
        {
            ControlTree = new MockControlTreeProvider(new[] { "ctrl1", "ctrl2" })
        };
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["OnClick"],
                    "bindings": {
                        "parameters": [
                            { "target": "text", "source": "ctrl1" }
                        ]
                    }
                }
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_LiteralSource_ShouldSkipControlRef()
    {
        // sourceType="literal" 的 source 是常量而非控件名（如 _stepSelector=Search），不校验控件引用。
        var context = new ConfigValidationContext
        {
            ControlTree = new MockControlTreeProvider(new[] { "ctrl1" })
        };
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["OnClick"],
                    "bindings": {
                        "parameters": [
                            { "target": "_stepSelector", "source": "Search", "sourceType": "literal" },
                            { "target": "keyword", "source": "ctrl1", "sourceType": "control" }
                        ]
                    }
                }
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_InvalidSourceRef_ShouldReturnError()
    {
        var context = new ConfigValidationContext
        {
            ControlTree = new MockControlTreeProvider(new[] { "ctrl1" })
        };
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["OnClick"],
                    "bindings": {
                        "parameters": [
                            { "target": "text", "source": "nonExistentCtrl" }
                        ]
                    }
                }
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual("ControlRef", diagnostics[0].RuleName);
        Assert.AreEqual(Severity.Error, diagnostics[0].Severity);
        Assert.IsTrue(diagnostics[0].Message.Contains("nonExistentCtrl"));
    }

    [TestMethod]
    public async Task ValidateAsync_MultipleParameters_ShouldValidateAll()
    {
        var context = new ConfigValidationContext
        {
            ControlTree = new MockControlTreeProvider(new[] { "ctrl1" })
        };
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["OnClick"],
                    "bindings": {
                        "parameters": [
                            { "target": "text", "source": "ctrl1" },
                            { "target": "value", "source": "nonExistent1" },
                            { "target": "enabled", "source": "nonExistent2" }
                        ]
                    }
                }
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(2, diagnostics.Count);
        Assert.IsTrue(diagnostics.All(d => d.RuleName == "ControlRef"));
    }

    [TestMethod]
    public async Task ValidateAsync_EmptySource_ShouldSkip()
    {
        var context = new ConfigValidationContext
        {
            ControlTree = new MockControlTreeProvider(new[] { "ctrl1" })
        };
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["OnClick"],
                    "bindings": {
                        "parameters": [
                            { "target": "text", "source": "" }
                        ]
                    }
                }
            }]
        }
        """;
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
