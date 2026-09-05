using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using CentralConsole.Testing.Mocks.EventTrigger;
using EventTriggerFramework.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventTriggerFramework.Tests
{
    [TestClass]
    public class CommandEngineTests : TestBase
    {
        [TestMethod]
        public void RunAll_CommandRegistration()
        {
            var result = RunTestGroup("CommandRegistration", builder =>
            {
                builder.Add("RegisterCommand_WithDelegate_ShouldStoreCommand", () =>
                {
                    var engine = new CommandEngineBase();
                    engine.RegisterCommand("testCmd", async (ctx, ct) => { await Task.CompletedTask; });
                    return engine.IsCommandRegistered("testCmd");
                });

                builder.Add("RegisterCommand_WithICommand_ShouldStoreCommand", () =>
                {
                    var engine = new CommandEngineBase();
                    var cmd = new MockCommand("mockCmd");
                    engine.RegisterCommand(cmd);
                    return engine.IsCommandRegistered("mockCmd");
                });

                builder.Add("RegisterCommand_Overwrite_ShouldReplace", () =>
                {
                    var engine = new CommandEngineBase();
                    engine.RegisterCommand("cmd", async (ctx, ct) => { await Task.CompletedTask; });
                    engine.RegisterCommand("cmd", async (ctx, ct) => { await Task.CompletedTask; });
                    return engine.IsCommandRegistered("cmd");
                });

                builder.Add("RegisterCommand_NullExecute_ShouldThrow", () =>
                {
                    var engine = new CommandEngineBase();
                    try
                    {
                        engine.RegisterCommand("cmd", (Func<CommandExecutionContext, CancellationToken, Task>)null!);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                });

                builder.Add("RegisterCommand_NullICommand_ShouldThrow", () =>
                {
                    var engine = new CommandEngineBase();
                    try
                    {
                        engine.RegisterCommand((ICommand)null!);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                });

                builder.Add("IsCommandRegistered_NonExistent_ShouldReturnFalse", () =>
                {
                    var engine = new CommandEngineBase();
                    return !engine.IsCommandRegistered("nonExistent");
                });

                builder.Add("GetRegisteredCommands_ShouldReturnAllNames", () =>
                {
                    var engine = new CommandEngineBase();
                    engine.RegisterCommand("a", async (ctx, ct) => { await Task.CompletedTask; });
                    engine.RegisterCommand("b", async (ctx, ct) => { await Task.CompletedTask; });
                    var names = engine.GetRegisteredCommands();
                    return names.Length == 2
                        && names.Contains("a")
                        && names.Contains("b");
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public async Task RunAll_CommandExecution()
        {
            var result = await RunTestGroupAsync("CommandExecution", builder =>
            {
                builder.Add("ExecuteAsync_ShouldInvokeCommand", async () =>
                {
                    var engine = new CommandEngineBase();
                    bool executed = false;
                    engine.RegisterCommand("cmd", async (ctx, ct) =>
                    {
                        executed = true;
                        await Task.CompletedTask;
                    });
                    var success = await engine.ExecuteAsync("cmd");
                    return success && executed;
                });

                builder.Add("ExecuteAsync_WithContext_ShouldPassContext", async () =>
                {
                    var engine = new CommandEngineBase();
                    CommandExecutionContext? capturedCtx = null;
                    engine.RegisterCommand("cmd", async (ctx, ct) =>
                    {
                        capturedCtx = ctx;
                        await Task.CompletedTask;
                    });
                    var success = await engine.ExecuteAsync("cmd");
                    return success && capturedCtx != null && capturedCtx.Name == "cmd";
                });

                builder.Add("ExecuteAsync_NonExistentCommand_ShouldReturnFalse", async () =>
                {
                    var engine = new CommandEngineBase();
                    var success = await engine.ExecuteAsync("nonExistent");
                    return !success;
                });

                builder.Add("ExecuteAsync_CommandThrows_ShouldReturnFalse", async () =>
                {
                    var engine = new CommandEngineBase();
                    engine.RegisterCommand("faulty", async (ctx, ct) =>
                    {
                        await Task.CompletedTask;
                        throw new InvalidOperationException("fail");
                    });
                    var success = await engine.ExecuteAsync("faulty");
                    return !success;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public async Task RunAll_UndoRedo()
        {
            var result = await RunTestGroupAsync("UndoRedo", builder =>
            {
                builder.Add("CanUndo_AfterExecute_ShouldReturnTrue", async () =>
                {
                    var engine = new CommandEngineBase();
                    engine.RegisterCommand("cmd", async (ctx, ct) => { await Task.CompletedTask; });
                    await engine.ExecuteAsync("cmd");
                    return engine.CanUndo;
                });

                builder.Add("CanRedo_Initially_ShouldReturnFalse", () =>
                {
                    var engine = new CommandEngineBase();
                    return Task.FromResult(!engine.CanRedo);
                });

                builder.Add("UndoAsync_ShouldInvokeUndo", async () =>
                {
                    var engine = new CommandEngineBase();
                    bool undone = false;
                    engine.RegisterCommand("cmd",
                        async (ctx, ct) => { await Task.CompletedTask; },
                        async (ctx, ct) => { undone = true; await Task.CompletedTask; });
                    await engine.ExecuteAsync("cmd");
                    var success = await engine.UndoAsync();
                    return success && undone;
                });

                builder.Add("UndoAsync_NoHistory_ShouldReturnFalse", async () =>
                {
                    var engine = new CommandEngineBase();
                    var success = await engine.UndoAsync();
                    return !success;
                });

                builder.Add("RedoAsync_AfterUndo_ShouldReExecute", async () =>
                {
                    var engine = new CommandEngineBase();
                    int execCount = 0;
                    engine.RegisterCommand("cmd",
                        async (ctx, ct) => { execCount++; await Task.CompletedTask; },
                        async (ctx, ct) => { await Task.CompletedTask; });
                    await engine.ExecuteAsync("cmd");
                    await engine.UndoAsync();
                    var success = await engine.RedoAsync();
                    return success && execCount == 2;
                });

                builder.Add("RedoAsync_NoRedoHistory_ShouldReturnFalse", async () =>
                {
                    var engine = new CommandEngineBase();
                    var success = await engine.RedoAsync();
                    return !success;
                });

                builder.Add("ClearHistory_ShouldClearUndoRedoStacks", async () =>
                {
                    var engine = new CommandEngineBase();
                    engine.RegisterCommand("cmd", async (ctx, ct) => { await Task.CompletedTask; });
                    await engine.ExecuteAsync("cmd");
                    engine.ClearHistory();
                    return !engine.CanUndo && !engine.CanRedo;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public async Task RunAll_DelegateCommand()
        {
            var result = await RunTestGroupAsync("DelegateCommand", builder =>
            {
                builder.Add("ExecuteAsync_ShouldInvokeDelegate", async () =>
                {
                    bool executed = false;
                    var cmd = new DelegateCommand("test", async ct =>
                    {
                        executed = true;
                        await Task.CompletedTask;
                    });
                    await cmd.ExecuteAsync();
                    return executed;
                });

                builder.Add("CanUndo_WithoutUndo_ShouldReturnFalse", () =>
                {
                    var cmd = new DelegateCommand("test", async ct =>
                    {
                        await Task.CompletedTask;
                    });
                    return Task.FromResult(!cmd.CanUndo);
                });

                builder.Add("CanUndo_WithUndo_ShouldReturnTrue", () =>
                {
                    var cmd = new DelegateCommand("test",
                        async ct => { await Task.CompletedTask; },
                        undo: async ct => { await Task.CompletedTask; });
                    return Task.FromResult(cmd.CanUndo);
                });

                builder.Add("UndoAsync_ShouldInvokeUndoDelegate", async () =>
                {
                    bool undone = false;
                    var cmd = new DelegateCommand("test",
                        async ct => { await Task.CompletedTask; },
                        undo: async ct => { undone = true; await Task.CompletedTask; });
                    await cmd.UndoAsync();
                    return undone;
                });

                builder.Add("Name_ShouldMatch", () =>
                {
                    var cmd = new DelegateCommand("myCmd", async ct => { await Task.CompletedTask; });
                    return Task.FromResult(cmd.Name == "myCmd");
                });

                builder.Add("Description_ShouldMatch", () =>
                {
                    var cmd = new DelegateCommand("cmd", async ct => { await Task.CompletedTask; }, description: "test desc");
                    return Task.FromResult(cmd.Description == "test desc");
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public async Task RunAll_CompositeCommand()
        {
            var result = await RunTestGroupAsync("CompositeCommand", builder =>
            {
                builder.Add("ExecuteAsync_ShouldExecuteAllSubCommands", async () =>
                {
                    var composite = new CompositeCommand("composite");
                    int callCount = 0;
                    composite.AddCommand(new DelegateCommand("c1", async ct => { callCount++; await Task.CompletedTask; }));
                    composite.AddCommand(new DelegateCommand("c2", async ct => { callCount++; await Task.CompletedTask; }));
                    await composite.ExecuteAsync();
                    return callCount == 2;
                });

                builder.Add("UndoAsync_ShouldUndoInReverseOrder", async () =>
                {
                    var composite = new CompositeCommand("composite");
                    var order = new List<int>();
                    composite.AddCommand(new DelegateCommand("c1",
                        async ct => { await Task.CompletedTask; },
                        undo: async ct => { order.Add(1); await Task.CompletedTask; }));
                    composite.AddCommand(new DelegateCommand("c2",
                        async ct => { await Task.CompletedTask; },
                        undo: async ct => { order.Add(2); await Task.CompletedTask; }));
                    await composite.ExecuteAsync();
                    await composite.UndoAsync();
                    return order.Count == 2 && order[0] == 2 && order[1] == 1;
                });

                builder.Add("CanUndo_AllCanUndo_ShouldReturnTrue", () =>
                {
                    var composite = new CompositeCommand("composite");
                    composite.AddCommand(new DelegateCommand("c1",
                        async ct => { await Task.CompletedTask; },
                        undo: async ct => { await Task.CompletedTask; }));
                    composite.AddCommand(new DelegateCommand("c2",
                        async ct => { await Task.CompletedTask; },
                        undo: async ct => { await Task.CompletedTask; }));
                    return Task.FromResult(composite.CanUndo);
                });

                builder.Add("CanUndo_SomeCannotUndo_ShouldReturnTrue", () =>
                {
                    var composite = new CompositeCommand("composite");
                    composite.AddCommand(new DelegateCommand("c1",
                        async ct => { await Task.CompletedTask; }));
                    composite.AddCommand(new DelegateCommand("c2",
                        async ct => { await Task.CompletedTask; },
                        undo: async ct => { await Task.CompletedTask; }));
                    return Task.FromResult(composite.CanUndo);
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public async Task RunAll_CommandEngineDispose()
        {
            var result = await RunTestGroupAsync("CommandEngineDispose", builder =>
            {
                builder.Add("Dispose_ShouldClearCommands", () =>
                {
                    var engine = new CommandEngineBase();
                    engine.RegisterCommand("cmd", async (ctx, ct) => { await Task.CompletedTask; });
                    engine.Dispose();
                    return Task.FromResult(!engine.IsCommandRegistered("cmd"));
                });

                builder.Add("Dispose_ShouldClearHistory", async () =>
                {
                    var engine = new CommandEngineBase();
                    engine.RegisterCommand("cmd", async (ctx, ct) => { await Task.CompletedTask; });
                    await engine.ExecuteAsync("cmd");
                    engine.Dispose();
                    return !engine.CanUndo;
                });

                builder.Add("Dispose_MultipleTimes_ShouldNotThrow", () =>
                {
                    var engine = new CommandEngineBase();
                    engine.Dispose();
                    engine.Dispose();
                    return Task.FromResult(true);
                });
            });

            result.AssertAllPassed();
        }
    }
}
