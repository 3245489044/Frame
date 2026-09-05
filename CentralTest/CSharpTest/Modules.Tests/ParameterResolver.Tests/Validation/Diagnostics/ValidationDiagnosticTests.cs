
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Tests.Diagnostics;

[TestClass]
public class ValidationDiagnosticTests
{
    [TestMethod]
    public void CreateError_ShouldSetCorrectProperties()
    {
        var diagnostic = ValidationDiagnostic.CreateError("TestRule", "测试错误");

        Assert.AreEqual("TestRule", diagnostic.RuleName);
        Assert.AreEqual(Severity.Error, diagnostic.Severity);
        Assert.AreEqual("测试错误", diagnostic.Message);
        Assert.IsNull(diagnostic.SourceLocation);
        Assert.IsNull(diagnostic.RepairSuggestion);
        Assert.AreNotEqual(default, diagnostic.Timestamp);
    }

    [TestMethod]
    public void CreateWarning_ShouldSetCorrectProperties()
    {
        var diagnostic = ValidationDiagnostic.CreateWarning("TestRule", "测试警告");

        Assert.AreEqual("TestRule", diagnostic.RuleName);
        Assert.AreEqual(Severity.Warning, diagnostic.Severity);
        Assert.AreEqual("测试警告", diagnostic.Message);
    }

    [TestMethod]
    public void CreateInfo_ShouldSetCorrectProperties()
    {
        var diagnostic = ValidationDiagnostic.CreateInfo("TestRule", "测试信息");

        Assert.AreEqual("TestRule", diagnostic.RuleName);
        Assert.AreEqual(Severity.Info, diagnostic.Severity);
        Assert.AreEqual("测试信息", diagnostic.Message);
    }

    [TestMethod]
    public void WithLocation_ShouldAttachSourceLocation()
    {
        var location = new SourceLocation("config.json", "$.controls[0]");
        var diagnostic = ValidationDiagnostic.CreateError("TestRule", "错误")
            .WithLocation(location);

        Assert.IsNotNull(diagnostic.SourceLocation);
        Assert.AreEqual("config.json", diagnostic.SourceLocation.FilePath);
        Assert.AreEqual("$.controls[0]", diagnostic.SourceLocation.JsonPath);
    }

    [TestMethod]
    public void WithSuggestion_ShouldAttachRepairSuggestion()
    {
        var diagnostic = ValidationDiagnostic.CreateError("TestRule", "错误")
            .WithSuggestion("请检查配置");

        Assert.IsNotNull(diagnostic.RepairSuggestion);
        Assert.AreEqual("请检查配置", diagnostic.RepairSuggestion);
    }

    [TestMethod]
    public void WithLocationAndSuggestion_ShouldChainCorrectly()
    {
        var location = new SourceLocation("config.json", "$.controls[0].name");
        var diagnostic = ValidationDiagnostic.CreateError("TestRule", "错误")
            .WithLocation(location)
            .WithSuggestion("请检查 name 字段");

        Assert.IsNotNull(diagnostic.SourceLocation);
        Assert.IsNotNull(diagnostic.RepairSuggestion);
        Assert.AreEqual("config.json", diagnostic.SourceLocation.FilePath);
        Assert.AreEqual("请检查 name 字段", diagnostic.RepairSuggestion);
    }

    [TestMethod]
    public void ToLogString_ShouldFormatCorrectly()
    {
        var diagnostic = ValidationDiagnostic.CreateError("TestRule", "测试错误");

        var log = diagnostic.ToLogString();

        Assert.IsTrue(log.Contains("[错误]"));
        Assert.IsTrue(log.Contains("TestRule"));
        Assert.IsTrue(log.Contains("测试错误"));
    }

    [TestMethod]
    public void ToLogString_WithLocation_ShouldIncludeLocation()
    {
        var location = new SourceLocation("config.json", "$.controls[0]");
        var diagnostic = ValidationDiagnostic.CreateError("TestRule", "错误")
            .WithLocation(location);

        var log = diagnostic.ToLogString();

        Assert.IsTrue(log.Contains("config.json"));
        Assert.IsTrue(log.Contains("$.controls[0]"));
    }

    [TestMethod]
    public void ToLogString_WithSuggestion_ShouldIncludeSuggestion()
    {
        var diagnostic = ValidationDiagnostic.CreateError("TestRule", "错误")
            .WithSuggestion("请修复");

        var log = diagnostic.ToLogString();

        Assert.IsTrue(log.Contains("请修复"));
    }

    [TestMethod]
    public void Record_WithExpressions_ShouldCreateVariants()
    {
        var original = ValidationDiagnostic.CreateError("Rule1", "原始错误");
        var modified = original with { Message = "修改后的错误" };

        Assert.AreEqual("原始错误", original.Message);
        Assert.AreEqual("修改后的错误", modified.Message);
        Assert.AreEqual(original.RuleName, modified.RuleName);
    }
}
