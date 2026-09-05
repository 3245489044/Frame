
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;
using WdUI;

namespace WdUI.Tests
{
    [TestClass]
    public class WdUIUsageCommandTests : TestBase
    {
        [TestMethod]
        public void RunAll_RegisterCommand()
        {
            var result = new TestGroupBuilder("RegisterCommand")
                .Add("RegisterCustomCommand_ShouldNotThrow", () =>
                {
                    WdUIUsage.RegisterCommand("customCmd", async (ctx, ct) =>
                    {
                        await Task.CompletedTask;
                    });
                    return true;
                })
                .Add("RegisterMultipleCommands_ShouldNotThrow", () =>
                {
                    WdUIUsage.RegisterCommand("cmd1", async (ctx, ct) => await Task.CompletedTask);
                    WdUIUsage.RegisterCommand("cmd2", async (ctx, ct) => await Task.CompletedTask);
                    return true;
                })
                .Add("RegisterOverwrite_ShouldNotThrow", () =>
                {
                    WdUIUsage.RegisterCommand("overwrite", async (ctx, ct) => await Task.CompletedTask);
                    WdUIUsage.RegisterCommand("overwrite", async (ctx, ct) => await Task.CompletedTask);
                    return true;
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public async Task RunAll_ExecuteCommandAsync()
        {
            var executionFlag = false;
            WdUIUsage.RegisterCommand("test.exec", async (ctx, ct) =>
            {
                executionFlag = true;
                await Task.CompletedTask;
            });

            var result = await new TestGroupBuilder("ExecuteCommandAsync")
                .Add("ExecuteRegisteredCommand_ShouldSucceed", async () =>
                {
                    executionFlag = false;
                    await WdUIUsage.ExecuteCommandAsync("test.exec");
                    return executionFlag;
                })
                .Add("ExecuteWithParameters_ShouldNotThrow", async () =>
                {
                    var parameters = new Dictionary<string, object?>
                    {
                        ["key1"] = "value1",
                        ["key2"] = 42
                    };
                    await WdUIUsage.ExecuteCommandAsync("test.exec", parameters);
                    return true;
                })
                .Add("ExecuteNonExistentCommand_ShouldNotThrow", async () =>
                {
                    await WdUIUsage.ExecuteCommandAsync("non.existent.command");
                    return true;
                })
                .RunAsync();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public async Task RunAll_ExecuteCommandsAsync()
        {
            var executionCount = 0;
            WdUIUsage.RegisterCommand("batch.cmd", async (ctx, ct) =>
            {
                Interlocked.Increment(ref executionCount);
                await Task.CompletedTask;
            });

            var result = await new TestGroupBuilder("ExecuteCommandsAsync")
                .Add("ExecuteMultipleCommands_ShouldExecuteAll", async () =>
                {
                    executionCount = 0;
                    var commands = new[] { "batch.cmd", "batch.cmd", "batch.cmd" };
                    await WdUIUsage.ExecuteCommandsAsync(commands);
                    return executionCount == 3;
                })
                .Add("ExecuteEmptyList_ShouldNotThrow", async () =>
                {
                    await WdUIUsage.ExecuteCommandsAsync(Array.Empty<string>());
                    return true;
                })
                .Add("ExecuteSingleCommand_ShouldExecuteOnce", async () =>
                {
                    executionCount = 0;
                    await WdUIUsage.ExecuteCommandsAsync(new[] { "batch.cmd" });
                    return executionCount == 1;
                })
                .RunAsync();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void RunAll_CommandEngine()
        {
            var result = new TestGroupBuilder("CommandEngine")
                .Add("CommandEngine_ShouldNotBeNull", () =>
                    WdUIUsage.CommandEngine != null)
                .Add("CommandEngine_ShouldBeSameInstance", () =>
                    WdUIUsage.CommandEngine == WdUIUsage.CommandEngine)
                .Run();

            result.AssertAllPassed(printSummary: true);
        }
    }
}
