
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;
using ParameterResolver.Validation.Rules.Structure;

namespace ParameterResolver.Validation.Tests.Rules;

[TestClass]
public class RequiredFieldRuleTests
{
    private static readonly IReadOnlyList<string> RequiredFields = new List<string> { "name", "type" };

    private readonly RequiredFieldRule _rule = new("$.controls[]", "控件", RequiredFields);
    private readonly ConfigValidationContext _context = new();

    [TestMethod]
    public async Task ValidateAsync_AllRequiredFieldsPresent_ShouldReturnNoDiagnostics()
    {
        var json = """
        {
            "controls": [{
                "name": "btn1",
                "type": "button",
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
    public async Task ValidateAsync_MissingName_ShouldReturnError()
    {
        var json = """
        {
            "controls": [{
                "type": "button"
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual("RequiredField_控件", diagnostics[0].RuleName);
        Assert.AreEqual(Severity.Error, diagnostics[0].Severity);
        Assert.IsTrue(diagnostics[0].Message.Contains("name"));
    }

    [TestMethod]
    public async Task ValidateAsync_MissingType_ShouldReturnError()
    {
        var json = """
        {
            "controls": [{
                "name": "btn1"
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.IsTrue(diagnostics[0].Message.Contains("type"));
    }

    [TestMethod]
    public async Task ValidateAsync_MissingBothNameAndType_ShouldReturnTwoErrors()
    {
        var json = """
        {
            "controls": [{}]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(2, diagnostics.Count);
        Assert.IsTrue(diagnostics.All(d => d.RuleName == "RequiredField_控件"));
    }

    [TestMethod]
    public async Task ValidateAsync_MultipleControls_ShouldValidateAll()
    {
        var json = """
        {
            "controls": [
                { "name": "btn1", "type": "button" },
                { "name": "panel1" },
                { "type": "textbox" }
            ]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(2, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_EmptyControls_ShouldReturnNoDiagnostics()
    {
        var json = """{"controls":[]}""";
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_NameIsEmptyString_ShouldReturnError()
    {
        var json = """
        {
            "controls": [{
                "name": "",
                "type": "button"
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.IsTrue(diagnostics[0].Message.Contains("name"));
    }
}
