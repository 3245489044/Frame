
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;
using ParameterResolver.Validation.Rules.Structure;

namespace ParameterResolver.Validation.Tests.Rules;

[TestClass]
public class EventValueTypeRuleTests
{
    private readonly EventValueTypeRule _rule = new();
    private readonly ConfigValidationContext _context = new();

    [TestMethod]
    public async Task ValidateAsync_ValidEvents_ShouldReturnNoDiagnostics()
    {
        var json = """
        {
            "controls": [
                {
                    "name": "btn1",
                    "events": {
                        "Click": ["OnClick"],
                        "DoubleClick": ["OnDoubleClick"]
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
    public async Task ValidateAsync_InvalidEventValueType_ShouldReturnError()
    {
        var json = """
        {
            "controls": [
                {
                    "name": "btn1",
                    "events": {
                        "Click": 123,
                        "DoubleClick": ["OnDoubleClick"]
                    }
                }
            ]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual("EventValueType", diagnostics[0].RuleName);
        Assert.AreEqual(Severity.Error, diagnostics[0].Severity);
        Assert.IsTrue(diagnostics[0].Message.Contains("string"));
    }

    [TestMethod]
    public async Task ValidateAsync_SingleStringShorthand_ShouldReturnNoDiagnostics()
    {
        // 契约（SKILL.md §2.4）：string 为单函数速记，等价于 ["X"]，与 string[] 均合法。
        var json = """
        {
            "controls": [
                {
                    "name": "btn1",
                    "events": {
                        "Click": "OnClick",
                        "DoubleClick": ["OnDoubleClick"]
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
    public async Task ValidateAsync_EventsWithBindings_ShouldSkipBindingsKey()
    {
        var json = """
        {
            "controls": [
                {
                    "name": "btn1",
                    "events": {
                        "Click": ["OnClick"],
                        "bindings": {
                            "parameters": [
                                { "target": "param1", "source": "ctrl1" }
                            ]
                        }
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
    public async Task ValidateAsync_NoEvents_ShouldReturnNoDiagnostics()
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
    public async Task ValidateAsync_NestedEvents_ShouldValidateAllLevels()
    {
        var json = """
        {
            "controls": [
                {
                    "name": "btn1",
                    "events": {
                        "Click": ["OnClick"]
                    },
                    "children": [
                        {
                            "name": "subBtn",
                            "events": {
                                "MouseEnter": 42
                            }
                        }
                    ]
                }
            ]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, _context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.IsTrue(diagnostics[0].Message.Contains("MouseEnter"));
    }
}
