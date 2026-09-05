using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using CentralConsole.Testing.Mocks.EventTrigger;
using EventTriggerFramework.Abstractions;
using EventTriggerFramework.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventTriggerFramework.Tests
{
    [TestClass]
    public class EventBindingCoreTests : TestBase
    {
        [TestMethod]
        public void RunAll_EventBindingInputOutput()
        {
            var result = RunTestGroup("EventBindingInputOutput", builder =>
            {
                builder.Add("EventBindingInput_DefaultValues", () =>
                {
                    var input = new EventBindingInput();
                    return input.ControlName == string.Empty
                        && input.EventType == string.Empty
                        && input.Value == string.Empty
                        && input.Config == null;
                });

                builder.Add("EventBindingInput_PropertyAssignment", () =>
                {
                    var config = new { key = "val" };
                    var input = new EventBindingInput
                    {
                        ControlName = "btnOK",
                        EventType = "Click",
                        Value = "testValue",
                        Config = config
                    };
                    return input.ControlName == "btnOK"
                        && input.EventType == "Click"
                        && input.Value == "testValue"
                        && input.Config == config;
                });

                builder.Add("EventBindingOutput_Success_ShouldSetIsSuccess", () =>
                {
                    var output = EventBindingOutput.Success("result", "func1", "outputTarget");
                    return output.IsSuccess
                        && output.ResultValue as string == "result"
                        && output.FunctionName == "func1"
                        && output.OutputTarget == "outputTarget"
                        && output.ErrorMessage == null;
                });

                builder.Add("EventBindingOutput_Success_NullValue_ShouldNotThrow", () =>
                {
                    var output = EventBindingOutput.Success(null);
                    return output.IsSuccess && output.ResultValue == null;
                });

                builder.Add("EventBindingOutput_Failure_ShouldSetErrorMessage", () =>
                {
                    var output = EventBindingOutput.Failure("something went wrong");
                    return !output.IsSuccess
                        && output.ErrorMessage == "something went wrong"
                        && output.ResultValue == null;
                });

                builder.Add("EventBindingOutput_Failure_NullMessage_ShouldNotThrow", () =>
                {
                    var output = EventBindingOutput.Failure(null);
                    return !output.IsSuccess && output.ErrorMessage == null;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_EventBindingInfo()
        {
            var result = RunTestGroup("EventBindingInfo", builder =>
            {
                builder.Add("EventBindingInfo_Record_ShouldCreate", () =>
                {
                    var inputs = new List<string> { "input1", "input2" }.AsReadOnly();
                    var info = new EventBindingInfo("funcName", "Click", inputs, "outputTarget", new[] { "funcName" });
                    return info.FunctionName == "funcName"
                        && info.EventType == "Click"
                        && info.Inputs.Count == 2
                        && info.Output == "outputTarget";
                });

                builder.Add("EventBindingInfo_Record_WithNullOutput", () =>
                {
                    var inputs = new List<string> { "input1" }.AsReadOnly();
                    var info = new EventBindingInfo("func", "Event", inputs, null, new[] { "func" });
                    return info.Output == null;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_ControlValueResult()
        {
            var result = RunTestGroup("ControlValueResult", builder =>
            {
                builder.Add("Success_ShouldSetIsSuccess", () =>
                {
                    var r = Result<object>.Success("ok");
                    return r.IsSuccess && (string)r.Value! == "ok";
                });

                builder.Add("Success_NullValue_ShouldNotThrow", () =>
                {
                    var r = Result<object>.Success(null);
                    return r.IsSuccess && r.Value == null;
                });

                builder.Add("Failure_ShouldSetIsSuccessFalse", () =>
                {
                    var r = Result<object>.Failure("error");
                    return !r.IsSuccess && r.ErrorMessage == "error";
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public async Task RunAll_EventBindingExecutor()
        {
            var result = await RunTestGroupAsync("EventBindingExecutor", builder =>
            {
                builder.Add("ExecuteAsync_WithCancellationToken_ShouldReturnSuccess", async () =>
                {
                    var pipeline = new MockBindingPipeline(EventBindingOutput.Success("mockResult"));
                    var output = await EventBindingExecutor.ExecuteAsync(
                        "btnOK", "Click", "value", pipeline, CancellationToken.None, null);
                    return output.IsSuccess && output.ResultValue as string == "mockResult";
                });

                builder.Add("ExecuteAsync_PipelineFailure_ShouldReturnFailure", async () =>
                {
                    var pipeline = new MockBindingPipeline(EventBindingOutput.Failure("pipeline error"));
                    var output = await EventBindingExecutor.ExecuteAsync(
                        "btnOK", "Click", "value", pipeline, default, null);
                    return !output.IsSuccess && output.ErrorMessage == "pipeline error";
                });

                builder.Add("ExecuteAsync_EmptyControlName_ShouldThrow", async () =>
                {
                    var pipeline = new MockBindingPipeline(EventBindingOutput.Success("ok"));
                    try
                    {
                        await EventBindingExecutor.ExecuteAsync(
                            "", "Click", "value", pipeline);
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                });

                builder.Add("ExecuteAsync_EmptyEventType_ShouldThrow", async () =>
                {
                    var pipeline = new MockBindingPipeline(EventBindingOutput.Success("ok"));
                    try
                    {
                        await EventBindingExecutor.ExecuteAsync(
                            "btnOK", "", "value", pipeline);
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                });

                builder.Add("ExecuteAsync_NullPipeline_ShouldThrow", async () =>
                {
                    try
                    {
                        await EventBindingExecutor.ExecuteAsync(
                            "btnOK", "Click", "value", null!);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                });

                builder.Add("ExecuteAsync_OnErrorCallback_ShouldBeCalledOnFailure", async () =>
                {
                    var pipeline = new MockBindingPipeline(EventBindingOutput.Failure("fail"));
                    string? capturedEventType = null;
                    string? capturedControlName = null;
                    string? capturedError = null;

                    await EventBindingExecutor.ExecuteAsync(
                        "btnOK", "Click", "value", pipeline, default, null,
                        (evt, ctrl, err) =>
                        {
                            capturedEventType = evt;
                            capturedControlName = ctrl;
                            capturedError = err;
                        });

                    return capturedEventType == "Click"
                        && capturedControlName == "btnOK"
                        && capturedError == "fail";
                });

                builder.Add("ExecuteAsync_PipelineThrows_ShouldReturnFailure", async () =>
                {
                    var pipeline = new MockBindingPipeline(
                        new Exception("unexpected error"));
                    var output = await EventBindingExecutor.ExecuteAsync(
                        "btnOK", "Click", "value", pipeline);
                    return !output.IsSuccess
                        && output.ErrorMessage.Contains("unexpected error");
                });
            });

            result.AssertAllPassed();
        }


        [TestMethod]
        public void RunAll_InMemoryStateStore()
        {
            var result = RunTestGroup("InMemoryStateStore", builder =>
            {
                builder.Add("Set_ShouldStoreValue", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("key1", "value1");
                    return store.Get("key1") == "value1";
                });

                builder.Add("Set_Overwrite_ShouldUpdateValue", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("key1", "oldValue");
                    store.Set("key1", "newValue");
                    return store.Get("key1") == "newValue";
                });

                builder.Add("Get_NonExistentKey_ShouldReturnNull", () =>
                {
                    var store = new InMemoryStateStore();
                    return store.Get("nonExistent") == null;
                });

                builder.Add("Has_ExistingKey_ShouldReturnTrue", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("exists", "val");
                    return store.Has("exists");
                });

                builder.Add("Has_NonExistentKey_ShouldReturnFalse", () =>
                {
                    var store = new InMemoryStateStore();
                    return !store.Has("nonExistent");
                });

                builder.Add("Clear_ShouldRemoveAll", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("key1", "val1");
                    store.Set("key2", "val2");
                    store.Clear();
                    return !store.Has("key1") && !store.Has("key2");
                });

                builder.Add("GetAllKeys_ShouldReturnAllKeys", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("a", "1");
                    store.Set("b", "2");
                    store.Set("c", "3");
                    var keys = store.GetAllKeys();
                    return keys.Count == 3
                        && keys.Contains("a")
                        && keys.Contains("b")
                        && keys.Contains("c");
                });

                builder.Add("RemoveByPrefix_ShouldRemoveMatchingKeys", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("prefix_key1", "val1");
                    store.Set("prefix_key2", "val2");
                    store.Set("other_key", "val3");
                    store.RemoveByPrefix("prefix_");
                    return !store.Has("prefix_key1")
                        && !store.Has("prefix_key2")
                        && store.Has("other_key");
                });

                builder.Add("RemoveByPrefix_NullPrefix_ShouldNotThrow", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("key", "val");
                    store.RemoveByPrefix(null);
                    return store.Has("key");
                });

                builder.Add("RemoveByPrefix_EmptyPrefix_ShouldNotThrow", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("key", "val");
                    store.RemoveByPrefix("");
                    return store.Has("key");
                });
            });

            result.AssertAllPassed();
        }
    }
}
