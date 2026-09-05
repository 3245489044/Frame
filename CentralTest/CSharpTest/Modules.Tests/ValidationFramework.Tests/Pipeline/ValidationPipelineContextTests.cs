
using System.Text.Json;
using ValidationFramework.Abstractions;
using ValidationFramework.Pipeline;

namespace ValidationFramework.Tests.Pipeline;

[TestClass]
public class ValidationPipelineContextTests
{
    private static readonly JsonElement EmptyConfig = JsonDocument.Parse("{}").RootElement;

    [TestMethod]
    public void 构造_应设置基础信息()
    {
        var ctx = new ValidationPipelineContext("Pipe", EmptyConfig, new ValidationContext());

        Assert.AreEqual("Pipe", ctx.PipelineName);
        Assert.AreEqual(JsonValueKind.Object, ctx.Config.ValueKind);
        Assert.IsNotNull(ctx.Context);
        Assert.AreEqual(0, ctx.Diagnostics.Count);
        Assert.AreEqual(0, ctx.ErrorCount);
        Assert.AreEqual(0, ctx.WarningCount);
        Assert.AreEqual(0, ctx.InfoCount);
        Assert.IsTrue(ctx.IsValid);
    }

    [TestMethod]
    public void BeginStage_应标记为运行中()
    {
        var ctx = new ValidationPipelineContext("Pipe", EmptyConfig, new ValidationContext());

        ctx.BeginStage("Structure");

        Assert.AreEqual(PipelineStageState.Running, ctx.StageStates["Structure"]);
    }

    [TestMethod]
    public void CompleteStage_无错误_应标记完成()
    {
        var ctx = new ValidationPipelineContext("Pipe", EmptyConfig, new ValidationContext());

        ctx.BeginStage("Structure");
        ctx.CompleteStage("Structure", new[] { ValidationDiagnostic.Warning("R1", "warn") });

        Assert.AreEqual(PipelineStageState.Completed, ctx.StageStates["Structure"]);
        Assert.AreEqual(1, ctx.WarningCount);
    }

    [TestMethod]
    public void CompleteStage_含错误_应标记失败()
    {
        var ctx = new ValidationPipelineContext("Pipe", EmptyConfig, new ValidationContext());

        ctx.BeginStage("Structure");
        ctx.CompleteStage("Structure", new[] { ValidationDiagnostic.Error("R1", "err") });

        Assert.AreEqual(PipelineStageState.Failed, ctx.StageStates["Structure"]);
        Assert.AreEqual(1, ctx.ErrorCount);
        Assert.IsFalse(ctx.IsValid);
    }

    [TestMethod]
    public void CompleteStage_应聚合多阶段诊断()
    {
        var ctx = new ValidationPipelineContext("Pipe", EmptyConfig, new ValidationContext());

        ctx.BeginStage("A");
        ctx.CompleteStage("A", new[] { ValidationDiagnostic.Error("R1", "err") });
        ctx.BeginStage("B");
        ctx.CompleteStage("B", new[] { ValidationDiagnostic.Info("R2", "info") });

        Assert.AreEqual(2, ctx.Diagnostics.Count);
        Assert.AreEqual(1, ctx.ErrorCount);
        Assert.AreEqual(1, ctx.InfoCount);
        Assert.AreEqual(2, ctx.StageStates.Count);
    }

    [TestMethod]
    public void CompleteStage_diagnostics为null_应抛异常()
    {
        var ctx = new ValidationPipelineContext("Pipe", EmptyConfig, new ValidationContext());

        Assert.ThrowsException<ArgumentNullException>(
            () => ctx.CompleteStage("A", null!));
    }

    [TestMethod]
    public void BuildReport_应生成聚合报告()
    {
        var ctx = new ValidationPipelineContext("Pipe", EmptyConfig, new ValidationContext());
        ctx.BeginStage("A");
        ctx.CompleteStage("A", new[] { ValidationDiagnostic.Warning("R1", "warn") });

        var report = ctx.BuildReport(TimeSpan.FromMilliseconds(10));

        Assert.AreEqual("Pipe", report.ValidatorName);
        Assert.AreEqual(1, report.Diagnostics.Count);
        Assert.AreEqual(1, report.WarningCount);
        Assert.IsTrue(report.IsValid);
        Assert.AreEqual(10, report.Duration.TotalMilliseconds);
    }

    [TestMethod]
    public void Diagnostics_应返回快照()
    {
        var ctx = new ValidationPipelineContext("Pipe", EmptyConfig, new ValidationContext());
        ctx.BeginStage("A");
        ctx.CompleteStage("A", new[] { ValidationDiagnostic.Error("R1", "err") });

        var snapshot = ctx.Diagnostics;

        ctx.BeginStage("B");
        ctx.CompleteStage("B", new[] { ValidationDiagnostic.Error("R2", "err") });

        Assert.AreEqual(1, snapshot.Count);
        Assert.AreEqual(2, ctx.Diagnostics.Count);
    }
}