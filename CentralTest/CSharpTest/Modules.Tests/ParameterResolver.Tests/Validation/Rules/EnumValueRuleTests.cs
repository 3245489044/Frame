
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;
using ParameterResolver.Validation.Rules.Structure;

namespace ParameterResolver.Validation.Tests.Rules;

[TestClass]
public class EnumValueRuleTests
{
    private static readonly IReadOnlySet<string> AllowedDockValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Left", "Right", "Top", "Bottom"
    };

    private readonly EnumValueRule _rule = new("dock", "停靠位置", AllowedDockValues);
    private readonly ConfigValidationContext _context = new();

    [TestMethod]
    public async Task ValidateAsync_ValidEnumValues_ShouldReturnNoDiagnostics()
    {
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "type": "button",
                "dock": "Left",
                "anchor": "Top"
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_InvalidDockValue_ShouldReturnError()
    {
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "type": "button",
                "dock": "InvalidDock"
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual("EnumValue_dock", diagnostics[0].RuleName);
        Assert.AreEqual(Severity.Error, diagnostics[0].Severity);
        Assert.IsTrue(diagnostics[0].Message.Contains("dock"));
    }

    [TestMethod]
    public async Task ValidateAsync_InvalidAnchorValue_ShouldReturnError()
    {
        var anchorRule = new EnumValueRule("anchor", "锚点位置", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Top", "Bottom" });
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "type": "button",
                "anchor": "InvalidAnchor"
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await anchorRule.ValidateAsync(config, _context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual("EnumValue_anchor", diagnostics[0].RuleName);
        Assert.IsTrue(diagnostics[0].Message.Contains("anchor"));
    }

    [TestMethod]
    public async Task ValidateAsync_MultipleInvalidValues_ShouldReturnAllErrors()
    {
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "type": "button",
                "dock": "BadDock",
                "anchor": "BadAnchor"
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual("EnumValue_dock", diagnostics[0].RuleName);
    }

    [TestMethod]
    public async Task ValidateAsync_NoEnumFields_ShouldReturnNoDiagnostics()
    {
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "type": "button"
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_CaseSensitivity_ShouldBeCaseSensitive()
    {
        var caseSensitiveRule = new EnumValueRule("dock", "停靠位置", new HashSet<string> { "Left", "Right", "Top", "Bottom" });
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "type": "button",
                "dock": "left"
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await caseSensitiveRule.ValidateAsync(config, _context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.IsTrue(diagnostics[0].Message.Contains("left"));
    }
}
