
using ValidationFramework.Abstractions;

namespace ValidationFramework.Tests.Abstractions;

[TestClass]
public class DiagnosticTests
{
    [TestMethod]
    public void Error_应创建错误级别诊断()
    {
        var d = ValidationDiagnostic.Error("RuleA", "错误消息");

        Assert.AreEqual("RuleA", d.RuleName);
        Assert.AreEqual("错误消息", d.Message);
        Assert.AreEqual(Severity.Error, d.Severity);
        Assert.IsNull(d.SourceLocation);
        Assert.IsNull(d.RepairSuggestion);
    }

    [TestMethod]
    public void Warning_应创建警告级别诊断()
    {
        var d = ValidationDiagnostic.Warning("RuleB", "警告消息");

        Assert.AreEqual(Severity.Warning, d.Severity);
    }

    [TestMethod]
    public void Info_应创建信息级别诊断()
    {
        var d = ValidationDiagnostic.Info("RuleC", "信息消息");

        Assert.AreEqual(Severity.Info, d.Severity);
    }

    [TestMethod]
    public void Error_带位置和建议_应正确设置()
    {
        var loc = new SourceLocation("file.json", "$.a.b");
        var d = ValidationDiagnostic.Error("RuleA", "消息", loc, "修复建议");

        Assert.AreEqual(loc, d.SourceLocation);
        Assert.AreEqual("修复建议", d.RepairSuggestion);
    }

    [TestMethod]
    public void WithLocation_应保留其他属性并更新位置()
    {
        var original = ValidationDiagnostic.Error("RuleA", "消息", null, "修复建议");
        var loc = new SourceLocation("file.json", "$.x");

        var updated = original.WithLocation(loc);

        Assert.AreEqual(loc, updated.SourceLocation);
        Assert.AreEqual("RuleA", updated.RuleName);
        Assert.AreEqual("修复建议", updated.RepairSuggestion);
        Assert.AreEqual(Severity.Error, updated.Severity);
    }

    [TestMethod]
    public void WithSuggestion_应保留其他属性并更新建议()
    {
        var loc = new SourceLocation("file.json");
        var original = ValidationDiagnostic.Warning("RuleB", "消息", loc);

        var updated = original.WithSuggestion("新建议");

        Assert.AreEqual("新建议", updated.RepairSuggestion);
        Assert.AreEqual(loc, updated.SourceLocation);
        Assert.AreEqual(Severity.Warning, updated.Severity);
    }

    [TestMethod]
    public void SourceLocation_ToString_仅有Source()
    {
        var loc = new SourceLocation("config.json");

        Assert.AreEqual("config.json", loc.ToString());
    }

    [TestMethod]
    public void SourceLocation_ToString_Source加JsonPath()
    {
        var loc = new SourceLocation("config.json", "$.views[0]");

        Assert.AreEqual("config.json - $.views[0]", loc.ToString());
    }

    [TestMethod]
    public void SourceLocation_ToString_含行列()
    {
        var loc = new SourceLocation("config.json", "$.a", 3, 5);

        Assert.AreEqual("config.json - $.a - (3,5)", loc.ToString());
    }

    [TestMethod]
    public void SourceLocation_ToString_仅有行()
    {
        var loc = new SourceLocation("config.json", null, 7, null);

        Assert.AreEqual("config.json - (7)", loc.ToString());
    }

    [TestMethod]
    public void SourceLocation_ToString_空位置_返回unknown()
    {
        var loc = new SourceLocation(null);

        Assert.AreEqual("(unknown)", loc.ToString());
    }

    [TestMethod]
    public void Diagnostic_ToString_包含级别规则与位置()
    {
        var d = ValidationDiagnostic.Error(
            "RuleA", "消息", new SourceLocation("config.json", "$.x"));

        Assert.AreEqual("[Error] RuleA: 消息 (config.json - $.x)", d.ToString());
    }
}