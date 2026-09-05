
using System.Text.Json;
using ValidationFramework.Abstractions;

namespace ValidationFramework.Tests.Abstractions;

[TestClass]
public class ValidationReportTests
{
    [TestMethod]
    public void Empty_应返回零计数空诊断()
    {
        var report = ValidationReport.Empty("Engine");

        Assert.AreEqual("Engine", report.ValidatorName);
        Assert.AreEqual(0, report.Diagnostics.Count);
        Assert.AreEqual(0, report.ErrorCount);
        Assert.AreEqual(0, report.WarningCount);
        Assert.AreEqual(0, report.InfoCount);
        Assert.IsTrue(report.IsValid);
    }

    [TestMethod]
    public void Create_应遍历计算各级别计数()
    {
        var diagnostics = new[]
        {
            ValidationDiagnostic.Error("R1", "err"),
            ValidationDiagnostic.Error("R2", "err"),
            ValidationDiagnostic.Warning("R3", "warn"),
            ValidationDiagnostic.Info("R4", "info")
        };

        var report = ValidationReport.Create("Engine", diagnostics);

        Assert.AreEqual(2, report.ErrorCount);
        Assert.AreEqual(1, report.WarningCount);
        Assert.AreEqual(1, report.InfoCount);
        Assert.IsFalse(report.IsValid);
        Assert.AreEqual(4, report.Diagnostics.Count);
    }

    [TestMethod]
    public void Create_仅警告无错误_应视为通过()
    {
        var report = ValidationReport.Create(
            "Engine",
            new[] { ValidationDiagnostic.Warning("R1", "warn") });

        Assert.IsTrue(report.IsValid);
        Assert.AreEqual(1, report.WarningCount);
    }

    [TestMethod]
    public void ProjectDiagnostics_默认不包含Info()
    {
        var report = ValidationReport.Create(
            "Engine",
            new[]
            {
                ValidationDiagnostic.Error("R1", "err"),
                ValidationDiagnostic.Warning("R2", "warn"),
                ValidationDiagnostic.Info("R3", "info")
            });

        var projected = report.ProjectDiagnostics();

        Assert.AreEqual(2, projected.Count);
    }

    [TestMethod]
    public void ProjectDiagnostics_包含Info_应返回全部()
    {
        var report = ValidationReport.Create(
            "Engine",
            new[]
            {
                ValidationDiagnostic.Error("R1", "err"),
                ValidationDiagnostic.Info("R2", "info")
            });

        var projected = report.ProjectDiagnostics(includeInfo: true);

        Assert.AreEqual(2, projected.Count);
    }

    [TestMethod]
    public void ProjectDiagnostics_应包含定位与修复建议字段()
    {
        var loc = new SourceLocation("file.json", "$.a.b", 1, 2);
        var report = ValidationReport.Create(
            "Engine",
            new[]
            {
                ValidationDiagnostic.Error("R1", "err", loc, "修复")
            });

        var item = report.ProjectDiagnostics().Single();
        var json = JsonSerializer.Serialize(item);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("Error", root.GetProperty("severity").GetString());
        Assert.AreEqual("R1", root.GetProperty("rule").GetString());
        Assert.AreEqual("err", root.GetProperty("message").GetString());
        Assert.AreEqual("$.a.b", root.GetProperty("path").GetString());
        Assert.AreEqual("file.json", root.GetProperty("source").GetString());
        Assert.AreEqual(1, root.GetProperty("line").GetInt32());
        Assert.AreEqual(2, root.GetProperty("column").GetInt32());
        Assert.AreEqual("修复", root.GetProperty("fix").GetString());
    }

    [TestMethod]
    public void ToJson_应包含报告结构()
    {
        var report = ValidationReport.Create(
            "Engine",
            new[] { ValidationDiagnostic.Warning("R1", "warn") });

        var json = report.ToJson();

        Assert.IsTrue(json.Contains("\"validatorName\": \"Engine\""));
        Assert.IsTrue(json.Contains("\"isValid\": true"));
        Assert.IsTrue(json.Contains("\"errorCount\": 0"));
        Assert.IsTrue(json.Contains("\"warningCount\": 1"));
        Assert.IsTrue(json.Contains("\"diagnostics\""));
    }

    [TestMethod]
    public void ToJson_应可反序列化()
    {
        var report = ValidationReport.Create(
            "Engine",
            new[] { ValidationDiagnostic.Error("R1", "err") });
        var json = report.ToJson();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("Engine", root.GetProperty("validatorName").GetString());
        Assert.AreEqual(false, root.GetProperty("isValid").GetBoolean());
        Assert.AreEqual(1, root.GetProperty("errorCount").GetInt32());
    }

    [TestMethod]
    public void Duration_应可设置()
    {
        var report = ValidationReport.Empty("Engine");

        report.Duration = TimeSpan.FromMilliseconds(42);

        Assert.AreEqual(42, report.Duration.TotalMilliseconds);
    }
}