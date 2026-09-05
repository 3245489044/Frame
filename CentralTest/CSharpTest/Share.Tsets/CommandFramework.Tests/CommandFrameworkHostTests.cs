using FunctionStorage;
using TestInfrastructure;
using CommandFramework.Abstractions;
using CommandFramework.Execution;
using CommandFramework.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommandFramework.Tests
{
    [TestClass]
    public class CommandFrameworkHostTests : TestBase
    {
        [TestMethod]
        public void RunAll_FactoryMethods()
        {
            var result = new TestGroupBuilder("FactoryMethods")
                .Add("CreateDefault_ShouldNotThrow", () =>
                {
                    using var host = CommandFrameworkHost.CreateDefault();
                    return host != null;
                })
                .Add("CreateDefault_ShouldHaveParser", () =>
                {
                    using var host = CommandFrameworkHost.CreateDefault();
                    return host.Parser != null;
                })
                .Add("CreateDefault_ShouldHaveEngine", () =>
                {
                    using var host = CommandFrameworkHost.CreateDefault();
                    return host.Engine != null;
                })
                .Add("CreateDefault_ShouldHavePipeline", () =>
                {
                    using var host = CommandFrameworkHost.CreateDefault();
                    return host.PipelineInstance != null;
                })
                .Add("CreateDefault_ShouldHavePipelineInstance", () =>
                {
                    using var host = CommandFrameworkHost.CreateDefault();
                    return host.PipelineInstance != null;
                })
                .Add("CreateCliHost_ShouldNotThrow", () =>
                {
                    using var host = CommandFrameworkHost.CreateCliHost();
                    return host != null;
                })
                .Add("CreateFullHost_ShouldNotThrow", () =>
                {
                    using var host = CommandFrameworkHost.CreateFullHost();
                    return host != null;
                })
                .Add("Create_WithConfigure_ShouldApplyOptions", () =>
                {
                    using var host = CommandFrameworkHost.Create(options =>
                    {
                        options.EnableStateStore = false;
                        options.EnableRegistry = false;
                    });
                    return host.StateStore == null;
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_ExecuteCommand()
        {
            var result = new TestGroupBuilder("ExecuteCommand")
                .Add("ExecuteCommand_Echo_ShouldSucceed", () =>
                {
                    using var host = CommandFrameworkHost.CreateDefault();
                    var cmdResult = host.ExecuteCommandAsync("echo Hello World").Result;
                    return !cmdResult.HasError;
                })
                .Add("ExecuteCommand_EmptyString_ShouldNotThrow", () =>
                {
                    using var host = CommandFrameworkHost.CreateDefault();
                    var cmdResult = host.ExecuteCommandAsync("").Result;
                    return cmdResult != null;
                })
                .Add("ExecuteCommands_Batch_ShouldReturnResults", () =>
                {
                    using var host = CommandFrameworkHost.CreateDefault();
                    var results = host.ExecuteCommandsAsync(new[]
                    {
                        "echo First",
                        "echo Second"
                    }).Result;
                    return results.Count == 2;
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_RegisterServices()
        {
            var result = new TestGroupBuilder("RegisterServices")
                .Add("RegisterFunction_ShouldWork", () =>
                {
                    FunctionStorageRegistry.Register("add", (int a, int b) => a + b);
                    var contains = FunctionStorageRegistry.Contains("add");
                    FunctionStorageRegistry.Clear();
                    return contains;
                })
                .Add("RegisterCommand_ShouldWork", () =>
                {
                    using var host = CommandFrameworkHost.CreateDefault();
                    host.RegisterCommand("greet",
                        async (ctx, ct) => { await Task.CompletedTask; });
                    return host.Engine!.IsCommandRegistered("greet");
                })
                .Add("RegisterCommand_WithoutEngine_ShouldThrow", () =>
                {
                    using var host = CommandFrameworkHost.Create(options =>
                    {
                        options.EnableEngine = false;
                    });
                    try
                    {
                        host.RegisterCommand("test",
                            async (ctx, ct) => { await Task.CompletedTask; });
                        return false;
                    }
                    catch (InvalidOperationException)
                    {
                        return true;
                    }
                })
                .Add("RegisterHandler_ShouldWork", () =>
                {
                    using var host = CommandFrameworkHost.CreateDefault();
                    host.RegisterHandler("myHandler", cmd => Task.FromResult(true));
                    return host.Engine!.IsCommandRegistered("myHandler");
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_Lifecycle()
        {
            var result = new TestGroupBuilder("Lifecycle")
                .Add("Dispose_ShouldNotThrow", () =>
                {
                    var host = CommandFrameworkHost.CreateDefault();
                    host.Dispose();
                    return true;
                })
                .Add("Dispose_MultipleTimes_ShouldNotThrow", () =>
                {
                    var host = CommandFrameworkHost.CreateDefault();
                    host.Dispose();
                    host.Dispose();
                    return true;
                })
                .Run();

            result.AssertAllPassed();
        }
    }
}
