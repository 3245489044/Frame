
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;
using ParameterResolver.Validation.Rules.Structure;

namespace ParameterResolver.Validation.Tests.Rules;

[TestClass]
public class LayoutFormatRuleTests
{
    private readonly LayoutFormatRule _rule = new();
    private readonly ConfigValidationContext _context = new();

    [TestMethod]
    public async Task ValidateAsync_ValidLayout_ShouldReturnNoDiagnostics()
    {
        var json = """
        {
            "controls": [
                {
                    "name": "panel1",
                    "layout": {
                        "x": 0.1,
                        "y": 0.2,
                        "width": 0.5,
                        "height": 0.3
                    }
                }
            ]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_OutOfRangeLayout_ShouldReturnWarning()
    {
        var json = """
        {
            "controls": [
                {
                    "name": "panel1",
                    "layout": {
                        "x": -0.1,
                        "y": 0.5,
                        "width": 1.5,
                        "height": 0.3
                    }
                }
            ]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(2, diagnostics.Count);
        Assert.IsTrue(diagnostics.All(d => d.RuleName == "LayoutFormat"));
        Assert.IsTrue(diagnostics.All(d => d.Severity == Severity.Warning));
    }

    [TestMethod]
    public async Task ValidateAsync_NoLayout_ShouldReturnNoDiagnostics()
    {
        var json = """
        {
            "controls": [
                {
                    "name": "btn1",
                    "type": "button"
                }
            ]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_BoundaryValues_ShouldBeValid()
    {
        var json = """
        {
            "controls": [
                {
                    "name": "panel1",
                    "layout": {
                        "x": 0.0,
                        "y": 1.0,
                        "width": 0.0,
                        "height": 1.0
                    }
                }
            ]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(0, diagnostics.Count);
    }
}
