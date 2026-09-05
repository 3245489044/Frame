using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using CentralConsole.Testing.Mocks.EventTrigger;
using EventTriggerFramework.Abstractions;
using EventTriggerFramework.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventTriggerFramework.Tests
{
    [TestClass]
    public class EventTriggerServiceBaseTests : TestBase
    {
        [TestMethod]
        public void RunAll_EventTriggerResult()
        {
            var result = RunTestGroup("EventTriggerResult", builder =>
            {
                builder.Add("SuccessResult_ShouldSetAllProperties", () =>
                {
                    var r = EventTriggerResult.SuccessResult("value", "btnOK", "Click", "func1", "output1", TimeSpan.FromMilliseconds(50));
                    return r.IsSuccess
                        && r.Value as string == "value"
                        && r.ControlName == "btnOK"
                        && r.EventType == "Click"
                        && r.FunctionName == "func1"
                        && r.OutputTarget == "output1"
                        && r.Message == "OK"
                        && r.ExecutionTime.TotalMilliseconds == 50;
                });

                builder.Add("SuccessResult_NullValue_ShouldNotThrow", () =>
                {
                    var r = EventTriggerResult.SuccessResult(null, "ctrl", "evt");
                    return r.IsSuccess && r.Value == null;
                });

                builder.Add("FailureResult_ShouldSetAllProperties", () =>
                {
                    var r = EventTriggerResult.FailureResult("error msg", "btnOK", "Click", "func1", TimeSpan.FromMilliseconds(30));
                    return !r.IsSuccess
                        && r.ErrorMessage == "error msg"
                        && r.ControlName == "btnOK"
                        && r.EventType == "Click"
                        && r.FunctionName == "func1"
                        && r.ExecutionTime.TotalMilliseconds == 30;
                });

                builder.Add("FailureResult_NullMessage_ShouldNotThrow", () =>
                {
                    var r = EventTriggerResult.FailureResult(null, "ctrl", "evt");
                    return !r.IsSuccess && r.ErrorMessage == null;
                });

                builder.Add("IsWorkflowSwitchEvent_Default_ShouldBeFalse", () =>
                {
                    var r = EventTriggerResult.SuccessResult("val", "ctrl", "evt");
                    return !r.IsWorkflowSwitchEvent;
                });

                builder.Add("IsWorkflowSwitchEvent_Set_ShouldWork", () =>
                {
                    var r = EventTriggerResult.SuccessResult("val", "ctrl", "evt");
                    r.IsWorkflowSwitchEvent = true;
                    return r.IsWorkflowSwitchEvent;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public async Task RunAll_EventTriggerServiceBase_TriggerEvent()
        {
            var result = await RunTestGroupAsync("EventTriggerServiceBase_TriggerEvent", builder =>
            {
                builder.Add("TriggerEventAsync_ShouldExecuteFullPipeline", async () =>
                {
                    var resolver = new MockBindingResolver("func1", new[] { "input1" }, "output1");
                    var extractor = new MockValueExtractor("extractedValue");
                    var service = new TestEventTriggerService(resolver, extractor);

                    var triggerResult = await service.TriggerEventAsync("btnOK", "Click", "value");
                    return triggerResult.IsSuccess
                        && triggerResult.ControlName == "btnOK"
                        && triggerResult.EventType == "Click"
                        && triggerResult.FunctionName == "func1"
                        && triggerResult.OutputTarget == "output1";
                });

                builder.Add("TriggerEventAsync_BindingNotFound_ShouldReturnFailure", async () =>
                {
                    var resolver = new MockBindingResolver(null, null, null);
                    var extractor = new MockValueExtractor(null);
                    var service = new TestEventTriggerService(resolver, extractor);

                    var triggerResult = await service.TriggerEventAsync("unknown", "Click", "value");
                    return !triggerResult.IsSuccess
                        && triggerResult.ErrorMessage.Contains("未找到事件绑定");
                });

                builder.Add("TriggerEventAsync_FunctionExecutionFailed_ShouldReturnFailure", async () =>
                {
                    var resolver = new MockBindingResolver("faultyFunc", new[] { "input1" }, "output1");
                    var extractor = new MockValueExtractor("val");
                    var service = new TestEventTriggerService(resolver, extractor, failFunction: true);

                    var triggerResult = await service.TriggerEventAsync("btnOK", "Click", "value");
                    return !triggerResult.IsSuccess
                        && triggerResult.ErrorMessage.Contains("执行失败");
                });

                builder.Add("TriggerEventAsync_WithDirectInputValues_ShouldUseThem", async () =>
                {
                    var resolver = new MockBindingResolver("func1", new[] { "input1" }, "output1");
                    var extractor = new MockValueExtractor("shouldNotBeUsed");
                    var service = new TestEventTriggerService(resolver, extractor);

                    var directInputs = new Dictionary<string, string?>
                    {
                        ["input1"] = "directValue"
                    };
                    var triggerResult = await service.TriggerEventAsync("btnOK", "Click", "value", directInputs);
                    return triggerResult.IsSuccess
                        && service.LastExtractedInputs != null
                        && service.LastExtractedInputs["input1"] == "directValue";
                });

                builder.Add("TriggerEventAsync_NoOutputTarget_ShouldStillSucceed", async () =>
                {
                    var resolver = new MockBindingResolver("func1", new[] { "input1" }, null);
                    var extractor = new MockValueExtractor("val");
                    var service = new TestEventTriggerService(resolver, extractor);

                    var triggerResult = await service.TriggerEventAsync("btnOK", "Click", "value");
                    return triggerResult.IsSuccess
                        && triggerResult.OutputTarget == null;
                });

                builder.Add("TriggerEventAsync_ExceptionInPipeline_ShouldReturnFailure", async () =>
                {
                    var resolver = new MockBindingResolver("func1", new[] { "input1" }, "output1");
                    var extractor = new MockValueExtractor("val");
                    var service = new TestEventTriggerService(resolver, extractor, throwException: true);

                    var triggerResult = await service.TriggerEventAsync("btnOK", "Click", "value");
                    return !triggerResult.IsSuccess
                        && triggerResult.ErrorMessage.Contains("异常");
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public async Task RunAll_EventTriggerServiceBase_ExtractInputValues()
        {
            var result = await RunTestGroupAsync("EventTriggerServiceBase_ExtractInputValues", builder =>
            {
                builder.Add("ExtractInputValuesAsync_WithStateStore_ShouldReturnValues", async () =>
                {
                    var stateStore = new InMemoryStateStore();
                    stateStore.Set("input1", "val1");
                    stateStore.Set("input2", "val2");

                    var resolver = new MockBindingResolver("func1", new[] { "input1", "input2" }, "output1");
                    var extractor = new MockValueExtractor(null);
                    var service = new TestEventTriggerService(resolver, extractor, stateStore: stateStore);

                    var triggerResult = await service.TriggerEventAsync("btnOK", "Click", "value");
                    return triggerResult.IsSuccess;
                });

                builder.Add("ExtractInputValuesAsync_EmptyInputs_ShouldReturnEmpty", async () =>
                {
                    var resolver = new MockBindingResolver("func1", Array.Empty<string>(), "output1");
                    var extractor = new MockValueExtractor(null);
                    var service = new TestEventTriggerService(resolver, extractor);

                    var triggerResult = await service.TriggerEventAsync("btnOK", "Click", "value");
                    return triggerResult.IsSuccess;
                });
            });

            result.AssertAllPassed();
        }
    }
}
