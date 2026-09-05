using BasicMethod.Pipeline;
using CentralConsole.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Core
{
    [TestClass]
    public class PipelineResultTests : TestBase
    {
        [TestMethod]
        public void RunAll_PipelineResultTests()
        {
            var result = RunTestGroup("PipelineResult", builder =>
            {
                builder.Add("Successful_ShouldCreateSuccessResult", () =>
                {
                    var result = PipelineResult.Successful("data");

                    Assert.IsTrue(result.IsSuccess);
                    Assert.IsFalse(result.IsAborted);
                    Assert.AreEqual("data", result.Data);
                    Assert.AreEqual(PipelineResultStatus.Success, result.Status);
                    Assert.IsFalse(result.HasErrors);
                });

                builder.Add("Successful_WithContext_ShouldUseProvidedContext", () =>
                {
                    var ctx = new PipelineContext();
                    var result = PipelineResult.Successful("data", ctx);

                    Assert.IsTrue(result.IsSuccess);
                    Assert.AreSame(ctx, result.Context);
                });

                builder.Add("Failed_ShouldCreateFailedResult", () =>
                {
                    var errors = new ErrorCollector();
                    errors.Add("ERR1", "Error message", ErrorLevel.Error);
                    var result = PipelineResult.Failed(errors);

                    Assert.IsFalse(result.IsSuccess);
                    Assert.AreEqual(PipelineResultStatus.Failed, result.Status);
                    Assert.IsTrue(result.HasErrors);
                    Assert.IsTrue(result.HasFatalErrors == false);
                });

                builder.Add("Failed_WithDataAndContext_ShouldCreateFailedResult", () =>
                {
                    var errors = new ErrorCollector();
                    errors.Add("ERR1", "Error", ErrorLevel.Error);
                    var ctx = new PipelineContext();
                    var result = PipelineResult.Failed("data", errors, ctx);

                    Assert.IsFalse(result.IsSuccess);
                    Assert.AreEqual("data", result.Data);
                    Assert.AreSame(ctx, result.Context);
                });

                builder.Add("Aborted_ShouldCreateAbortedResult", () =>
                {
                    var errors = new ErrorCollector();
                    var result = PipelineResult.Aborted(errors);

                    Assert.IsTrue(result.IsAborted);
                    Assert.IsFalse(result.IsSuccess);
                    Assert.AreEqual(PipelineResultStatus.Aborted, result.Status);
                });

                builder.Add("Aborted_WithContext_ShouldUseProvidedContext", () =>
                {
                    var errors = new ErrorCollector();
                    var ctx = new PipelineContext();
                    var result = PipelineResult.Aborted(errors, ctx);

                    Assert.IsTrue(result.IsAborted);
                    Assert.AreSame(ctx, result.Context);
                });

                builder.Add("CompletedWithWarnings_ShouldCreatePartialSuccess", () =>
                {
                    var errors = new ErrorCollector();
                    errors.Add("WARN1", "Warning", ErrorLevel.Warning);
                    var ctx = new PipelineContext();
                    var result = PipelineResult.CompletedWithWarnings("data", errors, ctx);

                    Assert.IsTrue(result.IsSuccess);
                    Assert.AreEqual(PipelineResultStatus.PartialSuccess, result.Status);
                    Assert.IsTrue(result.HasErrors);
                    Assert.AreEqual("data", result.Data);
                });

                builder.Add("ToGeneric_ShouldConvertCorrectly", () =>
                {
                    var result = PipelineResult.Successful("hello");
                    var generic = result.ToGeneric<string>();

                    Assert.IsTrue(generic.IsSuccess);
                    Assert.AreEqual("hello", generic.Data);
                });

                builder.Add("Generic_Successful_ShouldCreateSuccessResult", () =>
                {
                    var result = PipelineResult<string>.Successful("data");

                    Assert.IsTrue(result.IsSuccess);
                    Assert.IsFalse(result.IsAborted);
                    Assert.AreEqual("data", result.Data);
                    Assert.AreEqual("data", result.RawData);
                    Assert.IsFalse(result.HasErrors);
                });

                builder.Add("Generic_Successful_WithRawData_ShouldStoreBoth", () =>
                {
                    var result = PipelineResult<string>.Successful("processed", "raw");

                    Assert.IsTrue(result.IsSuccess);
                    Assert.AreEqual("processed", result.Data);
                    Assert.AreEqual("raw", result.RawData);
                });

                builder.Add("Generic_Failed_ShouldCreateFailedResult", () =>
                {
                    var errors = new ErrorCollector();
                    errors.Add("ERR1", "Error", ErrorLevel.Error);
                    var result = PipelineResult<string>.Failed(errors);

                    Assert.IsFalse(result.IsSuccess);
                    Assert.IsFalse(result.IsAborted);
                    Assert.IsNull(result.Data);
                    Assert.IsTrue(result.HasErrors);
                });

                builder.Add("Generic_Aborted_ShouldCreateAbortedResult", () =>
                {
                    var errors = new ErrorCollector();
                    var result = PipelineResult<string>.Aborted(errors);

                    Assert.IsFalse(result.IsSuccess);
                    Assert.IsTrue(result.IsAborted);
                    Assert.IsNull(result.Data);
                });

                builder.Add("Generic_CompletedWithWarnings_ShouldCreateSuccessWithWarnings", () =>
                {
                    var errors = new ErrorCollector();
                    errors.Add("WARN1", "Warning", ErrorLevel.Warning);
                    var result = PipelineResult<string>.CompletedWithWarnings("data", "raw", errors);

                    Assert.IsTrue(result.IsSuccess);
                    Assert.IsFalse(result.IsAborted);
                    Assert.AreEqual("data", result.Data);
                    Assert.IsTrue(result.HasErrors);
                });

                builder.Add("Generic_ToNonGeneric_ShouldConvertCorrectly", () =>
                {
                    var generic = PipelineResult<string>.Successful("hello");
                    var ctx = new PipelineContext();
                    var nonGeneric = generic.ToNonGeneric(ctx);

                    Assert.AreEqual("hello", nonGeneric.Data);
                    Assert.AreSame(ctx, nonGeneric.Context);
                    Assert.IsTrue(nonGeneric.IsSuccess);
                });

                builder.Add("Generic_ToNonGeneric_Aborted_ShouldPreserveStatus", () =>
                {
                    var errors = new ErrorCollector();
                    var generic = PipelineResult<string>.Aborted(errors);
                    var ctx = new PipelineContext();
                    var nonGeneric = generic.ToNonGeneric(ctx);

                    Assert.IsTrue(nonGeneric.IsAborted);
                    Assert.AreEqual(PipelineResultStatus.Aborted, nonGeneric.Status);
                });

                builder.Add("HasErrors_ShouldReflectErrorState", () =>
                {
                    var errors = new ErrorCollector();
                    errors.Add("ERR1", "Error", ErrorLevel.Error);
                    var result = PipelineResult.Failed(errors);

                    Assert.IsTrue(result.HasErrors);
                    Assert.IsFalse(result.HasFatalErrors);
                });

                builder.Add("HasFatalErrors_ShouldDetectFatalErrors", () =>
                {
                    var errors = new ErrorCollector();
                    errors.Add("FATAL", "Fatal error", ErrorLevel.Fatal);
                    var result = PipelineResult.Failed(errors);

                    Assert.IsTrue(result.HasErrors);
                    Assert.IsTrue(result.HasFatalErrors);
                });
            });
            result.AssertAllPassed();
        }
    }
}
