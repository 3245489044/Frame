
using System.Text.Json;
using ValidationFramework.Abstractions;
using ValidationFramework.Feedback;

namespace ValidationFramework.Tests.Feedback;

[TestClass]
public class FeedbackTests
{
    [TestMethod]
    public void ToLlmFeedback_有效报告_应返回isValid与零计数()
    {
        var report = ValidationReport.Empty("Engine");
        var formatter = new ValidationFeedbackFormatter();

        var json = formatter.ToLlmFeedback(report);

        Assert.IsTrue(json.Contains("\"isValid\": true"));
        Assert.IsTrue(json.Contains("\"errorCount\": 0"));
    }

    [TestMethod]
    public void ToLlmFeedback_应包含fixes数组()
    {
        var report = ValidationReport.Create(
            "Engine",
            new[] { ValidationDiagnostic.Error("R1", "err", null, "修复建议") });
        var formatter = new ValidationFeedbackFormatter();

        var json = formatter.ToLlmFeedback(report);

        using var doc = JsonDocument.Parse(json);
        var fixes = doc.RootElement.GetProperty("fixes");
        Assert.AreEqual(1, fixes.GetArrayLength());
        var item = fixes[0];
        Assert.AreEqual("R1", item.GetProperty("rule").GetString());
        Assert.AreEqual("err", item.GetProperty("message").GetString());
        Assert.AreEqual("修复建议", item.GetProperty("fix").GetString());
    }

    [TestMethod]
    public void ToLlmFeedback_应可反序列化()
    {
        var report = ValidationReport.Create(
            "Engine",
            new[] { ValidationDiagnostic.Error("R1", "err") });
        var formatter = new ValidationFeedbackFormatter();

        var json = formatter.ToLlmFeedback(report);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.AreEqual(false, root.GetProperty("isValid").GetBoolean());
        Assert.AreEqual(1, root.GetProperty("errorCount").GetInt32());
        Assert.AreEqual(1, root.GetProperty("fixes").GetArrayLength());
    }

    [TestMethod]
    public void ToLlmFeedback_默认不包含Info诊断()
    {
        var report = ValidationReport.Create(
            "Engine",
            new[] { ValidationDiagnostic.Info("R1", "info") });
        var formatter = new ValidationFeedbackFormatter();

        var json = formatter.ToLlmFeedback(report);

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(0, doc.RootElement.GetProperty("fixes").GetArrayLength());
    }

    [TestMethod]
    public void ToLlmFeedback_includeInfo_应包含Info诊断()
    {
        var report = ValidationReport.Create(
            "Engine",
            new[] { ValidationDiagnostic.Info("R1", "info") });
        var formatter = new ValidationFeedbackFormatter(includeInfo: true);

        var json = formatter.ToLlmFeedback(report);

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(1, doc.RootElement.GetProperty("fixes").GetArrayLength());
    }

    [TestMethod]
    public void ToTextSummary_通过且无警告_应返回OK()
    {
        var report = ValidationReport.Empty("Engine");
        var formatter = new ValidationFeedbackFormatter();

        var summary = formatter.ToTextSummary(report);

        Assert.IsTrue(summary.Contains("[OK]"));
        Assert.IsTrue(summary.Contains("Engine"));
    }

    [TestMethod]
    public void ToTextSummary_含诊断_应返回FAIL并列出()
    {
        var report = ValidationReport.Create(
            "Engine",
            new[]
            {
                ValidationDiagnostic.Error("R1", "错误"),
                ValidationDiagnostic.Warning("R2", "警告")
            });
        var formatter = new ValidationFeedbackFormatter();

        var summary = formatter.ToTextSummary(report);

        Assert.IsTrue(summary.Contains("[FAIL]"));
        Assert.IsTrue(summary.Contains("1 错误, 1 警告"));
        Assert.IsTrue(summary.Contains("R1: 错误"));
        Assert.IsTrue(summary.Contains("R2: 警告"));
    }

    [TestMethod]
    public void ToTextSummary_含修复建议_应输出修复行()
    {
        var report = ValidationReport.Create(
            "Engine",
            new[] { ValidationDiagnostic.Error("R1", "错误", null, "请修复") });
        var formatter = new ValidationFeedbackFormatter();

        var summary = formatter.ToTextSummary(report);

        Assert.IsTrue(summary.Contains("-> 修复: 请修复"));
    }

    [TestMethod]
    public void ToTextSummary_仅警告_应输出FAIL()
    {
        var report = ValidationReport.Create(
            "Engine",
            new[] { ValidationDiagnostic.Warning("R1", "警告") });
        var formatter = new ValidationFeedbackFormatter();

        var summary = formatter.ToTextSummary(report);

        Assert.IsTrue(summary.Contains("[FAIL]"));
    }

    [TestMethod]
    public void DefaultFeedbackSink_Emit_应调用日志委托()
    {
        var log = new List<string>();
        var sink = new DefaultFeedbackSink(log.Add);
        var report = ValidationReport.Empty("Engine");

        sink.Emit(report, null);

        Assert.AreEqual(1, log.Count);
        Assert.IsTrue(log[0].Contains("[OK]"));
    }

    [TestMethod]
    public void DefaultFeedbackSink_Emit_含json反馈_应输出两条()
    {
        var log = new List<string>();
        var sink = new DefaultFeedbackSink(log.Add);
        var report = ValidationReport.Empty("Engine");

        sink.Emit(report, "{\"json\":true}");

        Assert.AreEqual(2, log.Count);
        Assert.AreEqual("{\"json\":true}", log[1]);
    }

    [TestMethod]
    public void DefaultFeedbackSink_Emit_json反馈为null_仅输出摘要()
    {
        var log = new List<string>();
        var sink = new DefaultFeedbackSink(log.Add);
        var report = ValidationReport.Empty("Engine");

        sink.Emit(report, null);

        Assert.AreEqual(1, log.Count);
    }
}