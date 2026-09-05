using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using EventTriggerFramework.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventTriggerFramework.Tests
{
    [TestClass]
    public class EventTriggerExtensionsTests : TestBase
    {
        [TestMethod]
        public void RunAll_EventTypeResolver()
        {
            var result = RunTestGroup("EventTypeResolver", builder =>
            {
#pragma warning disable CS0618 // DefaultEventTypeResolver 标记为 Obsolete，测试保留验证
                builder.Add("DefaultResolver_ShouldReturnOperationType", () =>
                {
                    var resolver = new DefaultEventTypeResolver();
                    var eventType = resolver.ResolveEventType("btnOK", "Click");
                    return eventType == "Click";
                });

                builder.Add("DefaultResolver_WithConfig_ShouldReturnOperationType", () =>
                {
                    var resolver = new DefaultEventTypeResolver();
                    var config = new { key = "val" };
                    var eventType = resolver.ResolveEventType("btnOK", "Click", config);
                    return eventType == "Click";
                });
#pragma warning restore CS0618
            });

            result.AssertAllPassed();
        }


        [TestMethod]
        public void RunAll_ToOperationResult()
        {
            var result = RunTestGroup("ToOperationResult", builder =>
            {
                builder.Add("Success_ShouldConvert", () =>
                {
                    var output = EventBindingOutput.Success("result");
                    var opResult = output.ToOperationResult();
                    return opResult.Success && opResult.Data as string == "result";
                });

                builder.Add("Failure_ShouldConvert", () =>
                {
                    var output = EventBindingOutput.Failure("error");
                    var opResult = output.ToOperationResult();
                    return !opResult.Success && opResult.Message == "error";
                });
            });

            result.AssertAllPassed();
        }
    }
}
