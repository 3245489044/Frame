
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;
using ParameterResolver.Validation.Rules.Semantic;

namespace ParameterResolver.Validation.Tests.Rules;

[TestClass]
public class EventBindingFormatRuleTests
{
    private readonly EventBindingFormatRule _rule = new();
    private readonly ConfigValidationContext _context = new();

    [TestMethod]
    public async Task ValidateAsync_ValidBindings_ShouldReturnNoDiagnostics()
    {
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

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_OutputOnlyBindings_ShouldReturnNoDiagnostics()
    {
        // 契约（SKILL.md §2）：bindings 可仅含 output（output-only 绑定，parameters 缺省），不报"缺少 parameters"。
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["OnClick"],
                    "bindings": {
                        "output": "resultTextBox"
                    }
                }
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_BindingsNotObject_ShouldReturnError()
    {
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["OnClick"],
                    "bindings": "invalidString"
                }
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual("EventBindingFormat", diagnostics[0].RuleName);
        Assert.AreEqual(Severity.Error, diagnostics[0].Severity);
    }

    [TestMethod]
    public async Task ValidateAsync_ParametersNotArray_ShouldReturnError()
    {
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["OnClick"],
                    "bindings": {
                        "parameters": "notAnArray"
                    }
                }
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual("EventBindingFormat", diagnostics[0].RuleName);
    }

    [TestMethod]
    public async Task ValidateAsync_ParameterMissingTarget_ShouldReturnError()
    {
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["OnClick"],
                    "bindings": {
                        "parameters": [
                            { "source": "ctrl1" }
                        ]
                    }
                }
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.IsTrue(diagnostics[0].Message.Contains("target"));
    }

    [TestMethod]
    public async Task ValidateAsync_ParameterMissingSource_ShouldReturnError()
    {
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["OnClick"],
                    "bindings": {
                        "parameters": [
                            { "target": "text" }
                        ]
                    }
                }
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.IsTrue(diagnostics[0].Message.Contains("source"));
    }

    [TestMethod]
    public async Task ValidateAsync_NoBindings_ShouldReturnNoDiagnostics()
    {
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["OnClick"]
                }
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_NoEvents_ShouldReturnNoDiagnostics()
    {
        var json = """{"controls":[{"name":"btn1","type":"button"}]}""";
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_ValidSemantic_ShouldReturnNoWarning()
    {
        var json = """
        {
            "controls": [{
                "name": "list1",
                "events": {
                    "doubleclick": ["LoadConfig"],
                    "bindings": {
                        "parameters": [
                            { "target": "configPath", "source": "list1", "sourceType": "control", "semantic": "Selection" }
                        ]
                    }
                }
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_InvalidSemantic_ShouldReturnWarning()
    {
        var json = """
        {
            "controls": [{
                "name": "list1",
                "events": {
                    "doubleclick": ["LoadConfig"],
                    "bindings": {
                        "parameters": [
                            { "target": "configPath", "source": "list1", "semantic": "NoSuchSemantic" }
                        ]
                    }
                }
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(Severity.Warning, diagnostics[0].Severity);
        Assert.IsTrue(diagnostics[0].Message.Contains("semantic"));
    }
}
