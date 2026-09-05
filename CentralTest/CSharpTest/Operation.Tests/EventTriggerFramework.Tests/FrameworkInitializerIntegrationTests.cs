using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using CentralConsole.Testing.Mocks.EventTrigger;
using EventTriggerFramework.Abstractions;
using EventTriggerFramework.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventTriggerFramework.Tests
{
    [TestClass]
    public class FrameworkInitializerIntegrationTests : TestBase
    {
        [TestMethod]
        public void RunAll_Initialization()
        {
            var result = RunTestGroup("FrameworkInitializer_Initialization", builder =>
            {
                builder.Add("Initialize_ShouldCreateComponents", () =>
                {
                    var ctx = EventTriggerFrameworkInitializer.Initialize();
                    return ctx != null
                        && ctx.BindingResolver != null
                        && ctx.FunctionRegistry != null
                        && ctx.FunctionExecutor != null
                        && ctx.ValueExtractor != null
                        && ctx.StateStore != null;
                });

                builder.Add("Initialize_ShouldReturnValidComponents", () =>
                {
                    var ctx = EventTriggerFrameworkInitializer.Initialize();
                    return ctx.BindingResolver is IEventBindingResolver
                        && ctx.FunctionRegistry is IFunctionRegistry
                        && ctx.FunctionExecutor is IFunctionExecutor
                        && ctx.ValueExtractor is IValueExtractor
                        && ctx.StateStore is IStateStore;
                });

                builder.Add("Initialize_DefaultComponents_ShouldBeUsable", () =>
                {
                    var ctx = EventTriggerFrameworkInitializer.Initialize();
                    ctx.FunctionRegistry.Register("add", (int a, int b) => a + b);
                    var result = ctx.FunctionRegistry.Contains("add");
                    return result;
                });

                builder.Add("Initialize_WithCustomBindingResolver_ShouldUseIt", () =>
                {
                    var customResolver = new MockBindingResolver("func1", new[] { "input1" }, "output1");
                    var ctx = EventTriggerFrameworkInitializer.Initialize(options =>
                    {
                        options.EnableEventBus = false;
                        options.EnableCommands = false;
                        options.EnablePipeline = false;
                        options.EnableCore = true;
                        options.EnableWinForms = false;
                    });
                    return ctx.BindingResolver != null;
                });

                builder.Add("Initialize_WithCustomStateStore_ShouldUseIt", () =>
                {
                    var ctx = EventTriggerFrameworkInitializer.Initialize();
                    return ctx.StateStore != null;
                });

                builder.Add("Initialize_WithCustomValueExtractor_ShouldUseIt", () =>
                {
                    var ctx = EventTriggerFrameworkInitializer.Initialize();
                    return ctx.ValueExtractor != null;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_ComponentIntegration()
        {
            var result = RunTestGroup("FrameworkInitializer_ComponentIntegration", builder =>
            {
                builder.Add("FullPipeline_ShouldWork", () =>
                {
                    var ctx = EventTriggerFrameworkInitializer.Initialize();
                    ctx.FunctionRegistry.Register("greet", (string name) => $"Hello, {name}!");
                    var found = ctx.FunctionRegistry.TryGetDescriptor("greet", out var descriptor);
                    return found && descriptor.Name == "greet";
                });

                builder.Add("MultipleInitializations_ShouldNotShareState", () =>
                {
                    var ctx1 = EventTriggerFrameworkInitializer.Initialize();
                    ctx1.FunctionRegistry.Register("func", () => 42);

                    EventTriggerFrameworkInitializer.Shutdown();
                    var ctx2 = EventTriggerFrameworkInitializer.Initialize();

                    return !ctx2.FunctionRegistry.Contains("func");
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_EventBindingInfo()
        {
            var result = RunTestGroup("EventBindingInfo", builder =>
            {
                builder.Add("Create_ShouldSetProperties", () =>
                {
                    var inputs = new List<string> { "input1" }.AsReadOnly();
                    var info = new EventBindingInfo("func1", "Click", inputs, "output1", new[] { "func1" });
                    return info.FunctionName == "func1"
                        && info.EventType == "Click"
                        && info.Inputs.Count == 1
                        && info.Output == "output1";
                });

                builder.Add("Create_WithNullOutput_ShouldNotThrow", () =>
                {
                    var inputs = new List<string> { "input1" }.AsReadOnly();
                    var info = new EventBindingInfo("func1", "Click", inputs, null, new[] { "func1" });
                    return info.Output == null;
                });
            });

            result.AssertAllPassed();
        }
    }
}
