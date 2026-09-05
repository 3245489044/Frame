using TestInfrastructure;
using CommandFramework.Abstractions;
using CommandFramework.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Registry;

namespace CommandFramework.Tests
{
    [TestClass]
    public class FunctionExecutionTests : TestBase
    {
        [TestMethod]
        public void RunAll_FunctionRegistry()
        {
            var result = new TestGroupBuilder("FunctionRegistry")
                .Add("Register_ShouldBeContained", () =>
                {
                    var registry = new Share.Registry.FunctionRegistry();
                    registry.Register("hello", () => "world");
                    return registry.Contains("hello");
                })
                .Add("Register_EmptyName_ShouldThrow", () =>
                {
                    var registry = new Share.Registry.FunctionRegistry();
                    try
                    {
                        registry.Register("", () => { });
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                })
                .Add("Register_NullDelegate_ShouldThrow", () =>
                {
                    var registry = new Share.Registry.FunctionRegistry();
                    try
                    {
                        registry.Register("test", null!);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                })
                .Add("Unregister_ShouldRemove", () =>
                {
                    var registry = new Share.Registry.FunctionRegistry();
                    registry.Register("temp", () => { });
                    registry.Unregister("temp");
                    return !registry.Contains("temp");
                })
                .Add("TryGetDescriptor_Existing_ShouldReturnTrue", () =>
                {
                    var registry = new Share.Registry.FunctionRegistry();
                    registry.Register("add", (int a, int b) => a + b);
                    return registry.TryGetDescriptor("add", out var desc)
                        && desc != null
                        && desc.Parameters.Count == 2;
                })
                .Add("TryGetDescriptor_NonExisting_ShouldReturnFalse", () =>
                {
                    var registry = new Share.Registry.FunctionRegistry();
                    return !registry.TryGetDescriptor("nonExisting", out _);
                })
                .Add("GetRegisteredNames_ShouldReturnAll", () =>
                {
                    var registry = new Share.Registry.FunctionRegistry();
                    registry.Register("fn1", () => { });
                    registry.Register("fn2", () => { });
                    var names = registry.GetRegisteredNames().ToList();
                    return names.Count == 2
                        && names.Contains("fn1")
                        && names.Contains("fn2");
                })
                .Add("Clear_ShouldRemoveAll", () =>
                {
                    var registry = new Share.Registry.FunctionRegistry();
                    registry.Register("a", () => { });
                    registry.Register("b", () => { });
                    registry.Clear();
                    return !registry.Contains("a") && !registry.Contains("b");
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_FunctionExecutor()
        {
            var result = new TestGroupBuilder("FunctionExecutor")
                .Add("Execute_NoParams_ShouldSucceed", () =>
                {
                    var registry = new Share.Registry.FunctionRegistry();
                    var executor = new FunctionExecutor(registry);
                    registry.Register("ping", () => "pong");
                    var execResult = executor.ExecuteAsync("ping").Result;
                    return execResult.IsSuccess
                        && execResult.Value?.ToString() == "pong";
                })
                .Add("Execute_WithParams_ShouldSucceed", () =>
                {
                    var registry = new Share.Registry.FunctionRegistry();
                    var executor = new FunctionExecutor(registry);
                    registry.Register("add", (int a, int b) => a + b);
                    var execResult = executor.ExecuteAsync("add", new Dictionary<string, object>
                    {
                        ["a"] = 3,
                        ["b"] = 4
                    }).Result;
                    return execResult.IsSuccess
                        && execResult.Value?.ToString() == "7";
                })
                .Add("Execute_NonExistent_ShouldFail", () =>
                {
                    var registry = new Share.Registry.FunctionRegistry();
                    var executor = new FunctionExecutor(registry);
                    var execResult = executor.ExecuteAsync("nonExistent").Result;
                    return !execResult.IsSuccess;
                })
                .Add("Execute_AsyncFunction_ShouldSucceed", () =>
                {
                    var registry = new Share.Registry.FunctionRegistry();
                    var executor = new FunctionExecutor(registry);
                    registry.Register("asyncTask", async () =>
                    {
                        await Task.Delay(10);
                        return "done";
                    });
                    var execResult = executor.ExecuteAsync("asyncTask").Result;
                    return execResult.IsSuccess
                        && execResult.Value?.ToString() == "done";
                })
                .Add("Execute_WithPositionalParams_ShouldSucceed", () =>
                {
                    var registry = new Share.Registry.FunctionRegistry();
                    var executor = new FunctionExecutor(registry);
                    registry.Register("concat", (string a, string b) => a + b);
                    var execResult = executor.ExecuteAsync("concat", new object[] { "Hello", "World" }).Result;
                    return execResult.IsSuccess
                        && execResult.Value?.ToString() == "HelloWorld";
                })
                .Add("Execute_WithDefaultValue_ShouldUseDefault", () =>
                {
                    var registry = new Share.Registry.FunctionRegistry();
                    var executor = new FunctionExecutor(registry);
                    registry.Register("greet", (string name, string greeting = "Hello") => $"{greeting}, {name}!");
                    var execResult = executor.ExecuteAsync("greet", new Dictionary<string, object>
                    {
                        ["name"] = "Alice"
                    }).Result;
                    return execResult.IsSuccess
                        && execResult.Value?.ToString() == "Hello, Alice!";
                })
                .Add("Execute_DelegateDirectly_ShouldSucceed", () =>
                {
                    var registry = new Share.Registry.FunctionRegistry();
                    var executor = new FunctionExecutor(registry);
                    var execResult = executor.ExecuteAsync((int x) => x * 2, 5).Result;
                    return execResult.IsSuccess
                        && execResult.Value?.ToString() == "10";
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_DescriptorCreation()
        {
            var result = new TestGroupBuilder("DescriptorCreation")
                .Add("CreateDescriptorFromDelegate_ShouldCaptureParams", () =>
                {
                    var desc = Share.Registry.FunctionDescriptor.FromDelegate(
                        (string name, int count) => $"{name}:{count}");
                    return desc.Parameters.Count == 2
                        && desc.Parameters[0].Name == "name"
                        && desc.Parameters[0].Type == typeof(string)
                        && desc.Parameters[1].Name == "count"
                        && desc.Parameters[1].Type == typeof(int);
                })
                .Add("CreateDescriptorFromDelegate_NoParams_ShouldReturnEmpty", () =>
                {
                    var desc = Share.Registry.FunctionDescriptor.FromDelegate(
                        () => 42);
                    return desc.Parameters.Count == 0;
                })
                .Add("CreateDescriptorFromDelegate_ShouldDetectAsync", () =>
                {
                    var desc = Share.Registry.FunctionDescriptor.FromDelegate(
                        async () => await Task.FromResult(1));
                    return desc.IsAsync;
                })
                .Run();

            result.AssertAllPassed();
        }
    }
}
