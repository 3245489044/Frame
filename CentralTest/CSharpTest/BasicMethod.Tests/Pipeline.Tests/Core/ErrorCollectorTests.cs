using BasicMethod.Pipeline;
using CentralConsole.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Core
{
    [TestClass]
    public class ErrorCollectorTests : TestBase
    {
        [TestMethod]
        public void RunAll_ErrorCollectorTests()
        {
            var result = RunTestGroup("ErrorCollector", builder =>
            {
                builder.Add("Constructor_ShouldCreateEmptyCollector", () =>
                {
                    var collector = new ErrorCollector();

                    Assert.IsNotNull(collector.Errors);
                    Assert.AreEqual(0, collector.Errors.Count);
                    Assert.IsFalse(collector.HasErrors);
                    Assert.IsFalse(collector.HasFatalErrors);
                });

                builder.Add("Add_ShouldAddError", () =>
                {
                    var collector = new ErrorCollector();
                    collector.Add("ERR1", "Error message", ErrorLevel.Error);

                    Assert.AreEqual(1, collector.Errors.Count);
                    Assert.IsTrue(collector.HasErrors);
                });

                builder.Add("Add_WithException_ShouldStoreException", () =>
                {
                    var collector = new ErrorCollector();
                    var ex = new InvalidOperationException("test");
                    collector.Add("ERR1", "Error message", ErrorLevel.Error, ex);

                    Assert.AreEqual(1, collector.Errors.Count);
                    Assert.AreSame(ex, collector.Errors[0].Exception);
                });

                builder.Add("Add_WithContext_ShouldStoreContext", () =>
                {
                    var collector = new ErrorCollector();
                    collector.Add("ERR1", "Error message", ErrorLevel.Error, context: "test context");

                    Assert.AreEqual("test context", collector.Errors[0].Context);
                });

                builder.Add("Add_WithCodeAndMessage_ShouldCreateError", () =>
                {
                    var collector = new ErrorCollector();
                    collector.Add("ERR1", "Error message", ErrorLevel.Error);

                    var error = collector.Errors[0];
                    Assert.AreEqual("ERR1", error.Code);
                    Assert.AreEqual("Error message", error.Message);
                    Assert.AreEqual(ErrorLevel.Error, error.Level);
                });

                builder.Add("Add_Warning_ShouldNotSetHasFatalErrors", () =>
                {
                    var collector = new ErrorCollector();
                    collector.Add("WARN1", "Warning", ErrorLevel.Warning);

                    Assert.IsTrue(collector.HasErrors);
                    Assert.IsFalse(collector.HasFatalErrors);
                });

                builder.Add("Add_Fatal_ShouldSetHasFatalErrors", () =>
                {
                    var collector = new ErrorCollector();
                    collector.Add("FATAL1", "Fatal error", ErrorLevel.Fatal);

                    Assert.IsTrue(collector.HasErrors);
                    Assert.IsTrue(collector.HasFatalErrors);
                });

                builder.Add("Add_Info_ShouldNotSetHasErrors", () =>
                {
                    var collector = new ErrorCollector();
                    collector.Add("INFO1", "Info message", ErrorLevel.Info);

                    Assert.IsFalse(collector.HasErrors);
                    Assert.IsFalse(collector.HasFatalErrors);
                });

                builder.Add("AddRange_ShouldAddMultipleErrors", () =>
                {
                    var collector = new ErrorCollector();
                    var errors = new[]
                    {
                        new PipelineError("ERR1", "Error 1", ErrorLevel.Error),
                        new PipelineError("ERR2", "Error 2", ErrorLevel.Error)
                    };
                    collector.AddRange(errors);

                    Assert.AreEqual(2, collector.Errors.Count);
                });

                builder.Add("Clear_ShouldRemoveAllErrors", () =>
                {
                    var collector = new ErrorCollector();
                    collector.Add("ERR1", "Error", ErrorLevel.Error);
                    collector.Add("ERR2", "Error", ErrorLevel.Error);

                    Assert.AreEqual(2, collector.Errors.Count);
                    collector.Clear();
                    Assert.AreEqual(0, collector.Errors.Count);
                    Assert.IsFalse(collector.HasErrors);
                });

                builder.Add("HasFatalErrors_WithMixedLevels_ShouldDetectFatal", () =>
                {
                    var collector = new ErrorCollector();
                    collector.Add("WARN1", "Warning", ErrorLevel.Warning);
                    collector.Add("ERR1", "Error", ErrorLevel.Error);
                    collector.Add("FATAL1", "Fatal", ErrorLevel.Fatal);

                    Assert.IsTrue(collector.HasErrors);
                    Assert.IsTrue(collector.HasFatalErrors);
                });

                builder.Add("HasFatalErrors_WithOnlyErrors_ShouldNotDetectFatal", () =>
                {
                    var collector = new ErrorCollector();
                    collector.Add("ERR1", "Error", ErrorLevel.Error);
                    collector.Add("ERR2", "Error", ErrorLevel.Error);

                    Assert.IsTrue(collector.HasErrors);
                    Assert.IsFalse(collector.HasFatalErrors);
                });

                builder.Add("Errors_ShouldBeReadOnly", () =>
                {
                    var collector = new ErrorCollector();
                    collector.Add("ERR1", "Error", ErrorLevel.Error);

                    Assert.IsInstanceOfType(collector.Errors, typeof(IReadOnlyList<PipelineError>));
                });

                builder.Add("MultipleAdds_ShouldPreserveOrder", () =>
                {
                    var collector = new ErrorCollector();
                    collector.Add("ERR1", "First", ErrorLevel.Error);
                    collector.Add("ERR2", "Second", ErrorLevel.Error);
                    collector.Add("ERR3", "Third", ErrorLevel.Error);

                    Assert.AreEqual("ERR1", collector.Errors[0].Code);
                    Assert.AreEqual("ERR2", collector.Errors[1].Code);
                    Assert.AreEqual("ERR3", collector.Errors[2].Code);
                });

                builder.Add("PipelineError_Properties_ShouldBeCorrect", () =>
                {
                    var ex = new Exception("inner");
                    var error = new PipelineError("CODE", "Message", ErrorLevel.Fatal, ex, "ctx");

                    Assert.AreEqual("CODE", error.Code);
                    Assert.AreEqual("Message", error.Message);
                    Assert.AreEqual(ErrorLevel.Fatal, error.Level);
                    Assert.AreSame(ex, error.Exception);
                    Assert.AreEqual("ctx", error.Context);
                });
            });
            result.AssertAllPassed();
        }
    }
}
