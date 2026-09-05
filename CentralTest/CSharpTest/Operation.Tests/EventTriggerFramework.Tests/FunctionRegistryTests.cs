using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using EventTriggerFramework.Abstractions;
using EventTriggerFramework.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventTriggerFramework.Tests
{
    [TestClass]
    public class FunctionRegistryTests : TestBase
    {
        [TestMethod]
        public void RunAll_FunctionRegistry_RegisterAndUnregister()
        {
            var result = RunTestGroup("FunctionRegistry_RegisterAndUnregister", builder =>
            {
                builder.Add("Register_ShouldStoreFunction", () =>
                {
                    var registry = new FunctionRegistry();
                    registry.Register("add", (int a, int b) => a + b);
                    return registry.Contains("add");
                });

                builder.Add("Register_EmptyName_ShouldThrow", () =>
                {
                    var registry = new FunctionRegistry();
                    try
                    {
                        registry.Register("", (int a, int b) => a + b);
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                });

                builder.Add("Register_NullDelegate_ShouldThrow", () =>
                {
                    var registry = new FunctionRegistry();
                    try
                    {
                        registry.Register("func", null!);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                });

                builder.Add("Register_Overwrite_ShouldReplace", () =>
                {
                    var registry = new FunctionRegistry();
                    registry.Register("func", (int x) => x);
                    registry.Register("func", (string s) => s);
                    return registry.Contains("func");
                });

                builder.Add("Unregister_ShouldRemoveFunction", () =>
                {
                    var registry = new FunctionRegistry();
                    registry.Register("temp", () => 42);
                    registry.Unregister("temp");
                    return !registry.Contains("temp");
                });

                builder.Add("Unregister_NonExistent_ShouldNotThrow", () =>
                {
                    var registry = new FunctionRegistry();
                    registry.Unregister("nonExistent");
                    return true;
                });

                builder.Add("Contains_NonExistent_ShouldReturnFalse", () =>
                {
                    var registry = new FunctionRegistry();
                    return !registry.Contains("nonExistent");
                });

                builder.Add("GetRegisteredNames_ShouldReturnAllNames", () =>
                {
                    var registry = new FunctionRegistry();
                    registry.Register("a", () => 1);
                    registry.Register("b", () => 2);
                    var names = registry.GetRegisteredNames().ToList();
                    return names.Count == 2
                        && names.Contains("a")
                        && names.Contains("b");
                });

                builder.Add("Clear_ShouldRemoveAll", () =>
                {
                    var registry = new FunctionRegistry();
                    registry.Register("a", () => 1);
                    registry.Register("b", () => 2);
                    registry.Clear();
                    return !registry.Contains("a") && !registry.Contains("b");
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_FunctionRegistry_TryGetDescriptor()
        {
            var result = RunTestGroup("FunctionRegistry_TryGetDescriptor", builder =>
            {
                builder.Add("TryGetDescriptor_Existing_ShouldReturnTrue", () =>
                {
                    var registry = new FunctionRegistry();
                    registry.Register("hello", (string name) => $"Hello, {name}!");
                    var found = registry.TryGetDescriptor("hello", out var descriptor);
                    return found
                        && descriptor.Name == "hello"
                        && descriptor.Parameters.Count == 1
                        && descriptor.Parameters[0].Name == "name"
                        && descriptor.Parameters[0].Type == typeof(string);
                });

                builder.Add("TryGetDescriptor_NonExistent_ShouldReturnFalse", () =>
                {
                    var registry = new FunctionRegistry();
                    var found = registry.TryGetDescriptor("nonExistent", out var descriptor);
                    return !found && descriptor == null;
                });

                builder.Add("Descriptor_ShouldReflectAsyncReturnType", () =>
                {
                    var registry = new FunctionRegistry();
                    registry.Register("asyncFunc", async () => { await Task.Delay(1); });
                    var found = registry.TryGetDescriptor("asyncFunc", out var descriptor);
                    return found && descriptor.IsAsync;
                });

                builder.Add("Descriptor_ShouldReflectSyncReturnType", () =>
                {
                    var registry = new FunctionRegistry();
                    registry.Register("syncFunc", () => 42);
                    var found = registry.TryGetDescriptor("syncFunc", out var descriptor);
                    return found && !descriptor.IsAsync && descriptor.ReturnType == typeof(int);
                });

                builder.Add("Descriptor_WithDescription_ShouldMatch", () =>
                {
                    var registry = new FunctionRegistry();
                    registry.Register("described", (int x) => x * 2, "Double the input");
                    var found = registry.TryGetDescriptor("described", out var descriptor);
                    return found && descriptor.Description == "Double the input";
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public async Task RunAll_FunctionExecutor_ExecuteByName()
        {
            var result = await RunTestGroupAsync("FunctionExecutor_ExecuteByName", builder =>
            {
                builder.Add("ExecuteAsync_ByName_ShouldReturnResult", async () =>
                {
                    var registry = new FunctionRegistry();
                    registry.Register("add", (int a, int b) => a + b);
                    var executor = new FunctionExecutor(registry);
                    var execResult = await executor.ExecuteAsync("add", new Dictionary<string, object>
                    {
                        ["a"] = 3,
                        ["b"] = 4
                    });
                    return execResult.IsSuccess && (int)execResult.Value! == 7;
                });

                builder.Add("ExecuteAsync_NonExistent_ShouldReturnFailure", async () =>
                {
                    var registry = new FunctionRegistry();
                    var executor = new FunctionExecutor(registry);
                    var execResult = await executor.ExecuteAsync("nonExistent");
                    return !execResult.IsSuccess
                        && execResult.ErrorMessage.Contains("not found");
                });

                builder.Add("ExecuteAsync_NoParameters_ShouldWork", async () =>
                {
                    var registry = new FunctionRegistry();
                    registry.Register("constant", () => 42);
                    var executor = new FunctionExecutor(registry);
                    var execResult = await executor.ExecuteAsync("constant");
                    return execResult.IsSuccess && (int)execResult.Value! == 42;
                });

                builder.Add("ExecuteAsync_WithOptionalParameter_ShouldUseDefault", async () =>
                {
                    var registry = new FunctionRegistry();
                    registry.Register("greet", (string name, string greeting = "Hello") => $"{greeting}, {name}!");
                    var executor = new FunctionExecutor(registry);
                    var execResult = await executor.ExecuteAsync("greet", new Dictionary<string, object>
                    {
                        ["name"] = "World"
                    });
                    return execResult.IsSuccess && execResult.Value as string == "Hello, World!";
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public async Task RunAll_FunctionExecutor_ExecuteByDelegate()
        {
            var result = await RunTestGroupAsync("FunctionExecutor_ExecuteByDelegate", builder =>
            {
                builder.Add("ExecuteAsync_ByDelegate_ShouldReturnResult", async () =>
                {
                    var registry = new FunctionRegistry();
                    var executor = new FunctionExecutor(registry);
                    Func<int, int, int> add = (a, b) => a + b;
                    var execResult = await executor.ExecuteAsync(add, new Dictionary<string, object>
                    {
                        ["a"] = 10,
                        ["b"] = 20
                    });
                    return execResult.IsSuccess && (int)execResult.Value! == 30;
                });

                builder.Add("ExecuteAsync_ByDelegate_AsyncFunction", async () =>
                {
                    var registry = new FunctionRegistry();
                    var executor = new FunctionExecutor(registry);
                    Func<int, Task<int>> doubleAsync = async x =>
                    {
                        await Task.Delay(1);
                        return x * 2;
                    };
                    var execResult = await executor.ExecuteAsync(doubleAsync, new Dictionary<string, object>
                    {
                        ["x"] = 21
                    });
                    if (!execResult.IsSuccess)
                        throw new Exception($"Not success: {execResult.ErrorMessage}");
                    if ((int)execResult.Value! != 42)
                        throw new Exception($"Value was {execResult.Value}");
                    return true;
                });

                builder.Add("ExecuteAsync_ByDelegate_ThrowsException_ShouldReturnFailure", async () =>
                {
                    var registry = new FunctionRegistry();
                    var executor = new FunctionExecutor(registry);
                    Func<int, int> faulty = x => throw new InvalidOperationException("oops");
                    var execResult = await executor.ExecuteAsync(faulty, new Dictionary<string, object>
                    {
                        ["x"] = 1
                    });
                    return !execResult.IsSuccess && execResult.ErrorMessage.Contains("oops");
                });

                builder.Add("ExecuteAsync_ByDelegate_SingleParameter_ShouldPassDirectly", async () =>
                {
                    var registry = new FunctionRegistry();
                    var executor = new FunctionExecutor(registry);
                    Func<string, string> echo = s => s;
                    var execResult = await executor.ExecuteAsync(echo, "directValue");
                    return execResult.IsSuccess && execResult.Value as string == "directValue";
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_FunctionExecutionResult()
        {
            var result = RunTestGroup("FunctionExecutionResult", builder =>
            {
                builder.Add("Success_ShouldSetIsSuccessAndValue", () =>
                {
                    var fr = Result<object>.Success(42).WithExecutionTime(TimeSpan.FromMilliseconds(100));
                    return fr.IsSuccess && (int)fr.Value! == 42 && fr.ExecutionTime.TotalMilliseconds == 100;
                });

                builder.Add("Success_NullValue_ShouldNotThrow", () =>
                {
                    var fr = Result<object>.Success(null, null);
                    return fr.IsSuccess && fr.Value == null;
                });

                builder.Add("Failure_ShouldSetErrorMessage", () =>
                {
                    var fr = Result<object>.Failure("error occurred").WithExecutionTime(TimeSpan.FromMilliseconds(50));
                    return !fr.IsSuccess && fr.ErrorMessage == "error occurred";
                });

                builder.Add("Failure_NullMessage_ShouldNotThrow", () =>
                {
                    var fr = Result<object>.Failure(null);
                    return !fr.IsSuccess && fr.ErrorMessage == null;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_ParameterDescriptor()
        {
            var result = RunTestGroup("ParameterDescriptor", builder =>
            {
                builder.Add("DefaultValues_ShouldBeSet", () =>
                {
                    var pd = new ParameterDescriptor();
                    return pd.Name == string.Empty
                        && pd.Type == typeof(object)
                        && !pd.IsOptional
                        && pd.DefaultValue == null;
                });

                builder.Add("PropertyAssignment_ShouldWork", () =>
                {
                    var pd = new ParameterDescriptor
                    {
                        Name = "count",
                        Type = typeof(int),
                        IsOptional = true,
                        DefaultValue = 0
                    };
                    return pd.Name == "count"
                        && pd.Type == typeof(int)
                        && pd.IsOptional
                        && (int)pd.DefaultValue! == 0;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_FunctionDescriptor()
        {
            var result = RunTestGroup("FunctionDescriptor", builder =>
            {
                builder.Add("DefaultValues_ShouldBeSet", () =>
                {
                    var fd = new FunctionDescriptor();
                    return fd.Name == string.Empty
                        && fd.Description == string.Empty
                        && fd.ReturnType == typeof(void)
                        && !fd.IsAsync
                        && fd.Parameters.Count == 0;
                });

                builder.Add("PropertyAssignment_ShouldWork", () =>
                {
                    var fd = new FunctionDescriptor
                    {
                        Name = "test",
                        Description = "test func",
                        ReturnType = typeof(string),
                        IsAsync = true,
                        Parameters = new[]
                        {
                            new ParameterDescriptor { Name = "x", Type = typeof(int) }
                        }
                    };
                    return fd.Name == "test"
                        && fd.Description == "test func"
                        && fd.ReturnType == typeof(string)
                        && fd.IsAsync
                        && fd.Parameters.Count == 1;
                });
            });

            result.AssertAllPassed();
        }
    }
}
