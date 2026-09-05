using BasicMethod.Pipeline;
using CentralConsole.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Models
{
    [TestClass]
    public class PipelineStageTests : TestBase
    {
        [TestMethod]
        public void RunAll_PipelineStageTests()
        {
            var result = RunTestGroup("PipelineStage", builder =>
            {
                builder.Add("Constructor_ShouldSetProperties", () =>
                {
                    var stage = new PipelineStage("test", (input, errors, ctx) => input);

                    Assert.AreEqual("test", stage.Name);
                    Assert.AreEqual(0, stage.Order);
                    Assert.IsFalse(stage.IsFatal);
                });

                builder.Add("Constructor_WithOrder_ShouldSetOrder", () =>
                {
                    var stage = new PipelineStage("test", (input, errors, ctx) => input, order: 5);

                    Assert.AreEqual(5, stage.Order);
                });

                builder.Add("Constructor_WithIsFatal_ShouldSetIsFatal", () =>
                {
                    var stage = new PipelineStage("test", (input, errors, ctx) => input, isFatal: true);

                    Assert.IsTrue(stage.IsFatal);
                });

                builder.Add("Process_ShouldCallDelegate", () =>
                {
                    var stage = new PipelineStage("test", (input, errors, ctx) =>
                        input?.ToString()?.ToUpper());

                    var result = stage.Process("hello", new ErrorCollector(), new PipelineContext());
                    Assert.AreEqual("HELLO", result);
                });

                builder.Add("Process_WithNullInput_ShouldHandleGracefully", () =>
                {
                    var stage = new PipelineStage("test", (input, errors, ctx) => input);

                    var result = stage.Process(null, new ErrorCollector(), new PipelineContext());
                    Assert.IsNull(result);
                });

                builder.Add("Process_WithErrors_ShouldPassErrors", () =>
                {
                    var errors = new ErrorCollector();
                    var stage = new PipelineStage("test", (input, errors_, ctx) =>
                    {
                        errors_.Add("ERR1", "Test error", ErrorLevel.Error);
                        return input;
                    });

                    stage.Process("hello", errors, new PipelineContext());
                    Assert.IsTrue(errors.HasErrors);
                });

                builder.Add("Process_WithContext_ShouldPassContext", () =>
                {
                    PipelineContext? capturedCtx = null;
                    var stage = new PipelineStage("test", (input, errors, ctx) =>
                    {
                        capturedCtx = ctx;
                        return input;
                    });

                    var ctx = new PipelineContext();
                    stage.Process("hello", new ErrorCollector(), ctx);
                    Assert.AreSame(ctx, capturedCtx);
                });
            });
            result.AssertAllPassed();
        }
    }

    [TestClass]
    public class PipelineStatusTests : TestBase
    {
        [TestMethod]
        public void RunAll_PipelineStatusTests()
        {
            var result = RunTestGroup("PipelineStatus", builder =>
            {
                builder.Add("PipelineResultStatus_Values_ShouldBeCorrect", () =>
                {
                    Assert.AreEqual(0, (int)PipelineResultStatus.Success);
                    Assert.AreEqual(1, (int)PipelineResultStatus.PartialSuccess);
                    Assert.AreEqual(2, (int)PipelineResultStatus.Failed);
                    Assert.AreEqual(3, (int)PipelineResultStatus.Aborted);
                });

                builder.Add("PipelineResultStatus_Success_ShouldBeDefault", () =>
                {
                    PipelineResultStatus status = default;
                    Assert.AreEqual(PipelineResultStatus.Success, status);
                });

                builder.Add("PipelineResultStatus_ToString_ShouldWork", () =>
                {
                    Assert.AreEqual("Success", PipelineResultStatus.Success.ToString());
                    Assert.AreEqual("Failed", PipelineResultStatus.Failed.ToString());
                    Assert.AreEqual("Aborted", PipelineResultStatus.Aborted.ToString());
                });

                builder.Add("ErrorLevel_Values_ShouldBeCorrect", () =>
                {
                    Assert.AreEqual(0, (int)ErrorLevel.Info);
                    Assert.AreEqual(1, (int)ErrorLevel.Warning);
                    Assert.AreEqual(2, (int)ErrorLevel.Error);
                    Assert.AreEqual(3, (int)ErrorLevel.Fatal);
                });

                builder.Add("ErrorLevel_Info_ShouldBeDefault", () =>
                {
                    ErrorLevel level = default;
                    Assert.AreEqual(ErrorLevel.Info, level);
                });

                builder.Add("ErrorLevel_ToString_ShouldWork", () =>
                {
                    Assert.AreEqual("Info", ErrorLevel.Info.ToString());
                    Assert.AreEqual("Warning", ErrorLevel.Warning.ToString());
                    Assert.AreEqual("Error", ErrorLevel.Error.ToString());
                    Assert.AreEqual("Fatal", ErrorLevel.Fatal.ToString());
                });
            });
            result.AssertAllPassed();
        }
    }
}
