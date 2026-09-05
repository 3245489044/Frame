
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Tests.Diagnostics;

[TestClass]
public class ValidationReportTests
{
    [TestMethod]
    public void Constructor_ShouldInitializeEmptyReport()
    {
        var report = new ValidationReport("test-config.json");

        Assert.AreEqual("test-config.json", report.TargetName);
        Assert.IsTrue(report.IsValid);
        Assert.AreEqual(0, report.Diagnostics.Count);
        Assert.AreEqual(0, report.ErrorCount);
        Assert.AreEqual(0, report.WarningCount);
        Assert.AreEqual(0, report.InfoCount);
    }

    [TestMethod]
    public void AddDiagnostic_Error_ShouldSetIsValidFalse()
    {
        var report = new ValidationReport("test");

        report.AddDiagnostic(
            ValidationDiagnostic.CreateError("TestRule", "测试错误"));

        Assert.IsFalse(report.IsValid);
        Assert.AreEqual(1, report.ErrorCount);
        Assert.AreEqual(0, report.WarningCount);
        Assert.AreEqual(0, report.InfoCount);
    }

    [TestMethod]
    public void AddDiagnostic_Warning_ShouldKeepIsValidTrue()
    {
        var report = new ValidationReport("test");

        report.AddDiagnostic(
            ValidationDiagnostic.CreateWarning("TestRule", "测试警告"));

        Assert.IsTrue(report.IsValid);
        Assert.AreEqual(0, report.ErrorCount);
        Assert.AreEqual(1, report.WarningCount);
        Assert.AreEqual(0, report.InfoCount);
    }

    [TestMethod]
    public void AddDiagnostic_Info_ShouldKeepIsValidTrue()
    {
        var report = new ValidationReport("test");

        report.AddDiagnostic(
            ValidationDiagnostic.CreateInfo("TestRule", "测试信息"));

        Assert.IsTrue(report.IsValid);
        Assert.AreEqual(0, report.ErrorCount);
        Assert.AreEqual(0, report.WarningCount);
        Assert.AreEqual(1, report.InfoCount);
    }

    [TestMethod]
    public void MergeFrom_ShouldCombineDiagnostics()
    {
        var report1 = new ValidationReport("config1");
        report1.AddDiagnostic(
            ValidationDiagnostic.CreateError("Rule1", "错误1"));

        var report2 = new ValidationReport("config2");
        report2.AddDiagnostic(
            ValidationDiagnostic.CreateWarning("Rule2", "警告2"));

        var merged = new ValidationReport("merged");

        merged.MergeFrom(report1);
        merged.MergeFrom(report2);

        Assert.AreEqual(2, merged.Diagnostics.Count);
        Assert.AreEqual(1, merged.ErrorCount);
        Assert.AreEqual(1, merged.WarningCount);
        Assert.IsFalse(merged.IsValid);
    }

    [TestMethod]
    public void ToJson_ShouldProduceValidJson()
    {
        var report = new ValidationReport("test.json");
        report.AddDiagnostic(
            ValidationDiagnostic.CreateError("TestRule", "测试错误"));

        var json = report.ToJson();

        Assert.IsNotNull(json);
        Assert.IsTrue(json.Contains("test.json"));
        Assert.IsTrue(json.Contains("TestRule"));
        Assert.IsTrue(json.Contains("\\u6D4B\\u8BD5\\u9519\\u8BEF"));
        Assert.IsTrue(json.Contains("isValid"));
    }

    [TestMethod]
    public void ToDiagnosticString_ShouldFormatCorrectly()
    {
        var report = new ValidationReport("test.json");
        report.AddDiagnostic(
            ValidationDiagnostic.CreateError("TestRule", "测试错误"));

        var text = report.ToDiagnosticString();

        Assert.IsNotNull(text);
        Assert.IsTrue(text.Contains("test.json"));
        Assert.IsTrue(text.Contains("TestRule"));
        Assert.IsTrue(text.Contains("测试错误"));
        Assert.IsTrue(text.Contains("失败"));
    }

    [TestMethod]
    public void GetBySeverity_ShouldFilterCorrectly()
    {
        var report = new ValidationReport("test");
        report.AddDiagnostic(ValidationDiagnostic.CreateError("Rule1", "错误1"));
        report.AddDiagnostic(ValidationDiagnostic.CreateWarning("Rule2", "警告2"));
        report.AddDiagnostic(ValidationDiagnostic.CreateInfo("Rule3", "信息3"));

        var errors = report.GetBySeverity(Severity.Error);
        var warnings = report.GetBySeverity(Severity.Warning);
        var infos = report.GetBySeverity(Severity.Info);

        Assert.AreEqual(1, errors.Count);
        Assert.AreEqual(1, warnings.Count);
        Assert.AreEqual(1, infos.Count);
    }

    [TestMethod]
    public void GetByRule_ShouldFilterCorrectly()
    {
        var report = new ValidationReport("test");
        report.AddDiagnostic(ValidationDiagnostic.CreateError("Rule1", "错误1"));
        report.AddDiagnostic(ValidationDiagnostic.CreateError("Rule1", "错误2"));
        report.AddDiagnostic(ValidationDiagnostic.CreateWarning("Rule2", "警告1"));

        var rule1Diagnostics = report.GetByRule("Rule1");
        var rule2Diagnostics = report.GetByRule("Rule2");

        Assert.AreEqual(2, rule1Diagnostics.Count);
        Assert.AreEqual(1, rule2Diagnostics.Count);
    }
}
