using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventTriggerFramework.Tests
{
    [TestClass]
    public class EventPipelineRegistryTests : TestBase
    {
        [TestMethod]
        public void RunAll_EventRequest()
        {
            var result = RunTestGroup("EventRequest", builder =>
            {
                builder.Add("FromConfig_ShouldCreateRequest", () =>
                {
                    var request = EventRequest.FromConfig("Click", "btnOK", "data");
                    return request.EventType == "Click"
                        && request.ControlName == "btnOK"
                        && request.Data as string == "data"
                        && request.Source == "config";
                });

                builder.Add("FromCode_ShouldCreateRequest", () =>
                {
                    var request = EventRequest.FromCode("Click", "btnOK", "data");
                    return request.EventType == "Click"
                        && request.ControlName == "btnOK"
                        && request.Data as string == "data"
                        && request.Source == "code";
                });

                builder.Add("FromCommand_ShouldCreateRequest", () =>
                {
                    var parameters = new Dictionary<string, object> { ["key"] = "val" };
                    var request = EventRequest.FromCommand("Click", "btnOK", parameters);
                    return request.EventType == "Click"
                        && request.ControlName == "btnOK"
                        && request.Source == "command"
                        && request.Parameters != null
                        && request.Parameters["key"] as string == "val";
                });

                builder.Add("FromResize_ShouldCreateRequest", () =>
                {
                    var request = EventRequest.FromResize("form1", 800, 600);
                    return request.EventType == "resize"
                        && request.ControlName == "form1"
                        && request.Source == "resize"
                        && request.Data != null;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_EventResult()
        {
            var result = RunTestGroup("EventResult", builder =>
            {
                builder.Add("Success_ShouldSetIsSuccess", () =>
                {
                    var r = EventResult.Success("ok");
                    return r.IsSuccess && r.Value as string == "ok";
                });

                builder.Add("Failure_ShouldSetErrorMessage", () =>
                {
                    var r = EventResult.Failure("error");
                    return !r.IsSuccess && r.ErrorMessage == "error";
                });

                builder.Add("FromResult_Success_ShouldConvert", () =>
                {
                    var bindingOutput = EventBindingOutput.Success("result");
                    var r = EventResult.FromResult(bindingOutput);
                    return r.IsSuccess && r.Value as string == "result";
                });

                builder.Add("FromResult_Failure_ShouldConvert", () =>
                {
                    var bindingOutput = EventBindingOutput.Failure("binding error");
                    var r = EventResult.FromResult(bindingOutput);
                    return !r.IsSuccess && r.ErrorMessage == "binding error";
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_EventBindingOutput()
        {
            var result = RunTestGroup("EventBindingOutput", builder =>
            {
                builder.Add("Success_ShouldSetProperties", () =>
                {
                    var output = EventBindingOutput.Success("result", "func1", "outputTarget");
                    return output.IsSuccess
                        && output.ResultValue as string == "result"
                        && output.FunctionName == "func1"
                        && output.OutputTarget == "outputTarget";
                });

                builder.Add("Failure_ShouldSetErrorMessage", () =>
                {
                    var output = EventBindingOutput.Failure("error");
                    return !output.IsSuccess && output.ErrorMessage == "error";
                });
            });

            result.AssertAllPassed();
        }
    }
}
