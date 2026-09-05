
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;
using ParameterResolver.Validation.Rules.Semantic;

namespace ParameterResolver.Validation.Tests.Rules;

[TestClass]
public class ParameterMappingFormatRuleTests
{
    private readonly ParameterMappingFormatRule _rule = new();
    private readonly ConfigValidationContext _context = new();

    [TestMethod]
    public async Task ValidateAsync_ValidMapping_ShouldReturnNoDiagnostics()
    {
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["OnClick"]
                }
            }],
            "bindings": {
                "parameters": [
                    { "target": "text", "source": "ctrl1", "converter": "ToString" }
                ]
            }
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_InvalidTargetFormat_ShouldReturnError()
    {
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["OnClick"]
                }
            }],
            "bindings": {
                "parameters": [
                    { "target": "123invalid", "source": "ctrl1" }
                ]
            }
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual("ParameterMappingFormat", diagnostics[0].RuleName);
        Assert.AreEqual(Severity.Warning, diagnostics[0].Severity);
    }

    [TestMethod]
    public async Task ValidateAsync_InvalidSourceFormat_ShouldReturnError()
    {
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["OnClick"]
                }
            }],
            "bindings": {
                "parameters": [
                    { "target": "text", "source": "@bad source!" }
                ]
            }
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual("ParameterMappingFormat", diagnostics[0].RuleName);
    }

    [TestMethod]
    public async Task ValidateAsync_MultipleInvalidMappings_ShouldReturnAllErrors()
    {
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "events": {
                    "Click": ["OnClick"]
                }
            }],
            "bindings": {
                "parameters": [
                    { "target": "123bad", "source": "ctrl1" },
                    { "target": "text", "source": "@bad" },
                    { "target": "also-bad", "source": "ctrl2" }
                ]
            }
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(3, diagnostics.Count);
        Assert.IsTrue(diagnostics.All(d => d.RuleName == "ParameterMappingFormat"));
    }

    [TestMethod]
    public async Task ValidateAsync_NoParameters_ShouldReturnNoDiagnostics()
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
}
