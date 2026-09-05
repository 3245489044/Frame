using TestInfrastructure;
using CommandFramework.Abstractions;
using CommandFramework.Execution;
using CommandFramework.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommandFramework.Tests
{
    [TestClass]
    public class CommandEngineTests : TestBase
    {
        [TestMethod]
        public void RunAll_RegisterCommand()
        {
            var result = new TestGroupBuilder("RegisterCommand")
                .Add("RegisterCommand_Delegate_ShouldBeRegistered", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    engine.RegisterCommand("test", async (ctx, ct) => { await Task.CompletedTask; });
                    return engine.IsCommandRegistered("test");
                })
                .Add("RegisterCommand_NullExecute_ShouldThrow", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    try
                    {
                        engine.RegisterCommand("test", null!);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                })
                .Add("RegisterCommand_ICommand_ShouldBeRegistered", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    var cmd = new DelegateCommand("delegateCmd", ct => Task.CompletedTask);
                    engine.RegisterCommand(cmd);
                    return engine.IsCommandRegistered("delegateCmd");
                })
                .Add("RegisterCommand_ICommand_Null_ShouldThrow", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    try
                    {
                        engine.RegisterCommand((ICommand)null!);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_Execute()
        {
            var result = new TestGroupBuilder("Execute")
                .Add("Execute_SimpleCommand_ShouldSucceed", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    var executed = false;
                    engine.RegisterCommand("hello", async (ctx, ct) =>
                    {
                        executed = true;
                        await Task.CompletedTask;
                    });
                    var success = engine.ExecuteAsync("hello").Result;
                    return success && executed;
                })
                .Add("Execute_NonExistentCommand_ShouldFail", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    var success = engine.ExecuteAsync("nonexistent").Result;
                    return !success;
                })
                .Add("Execute_CommandWithContext_ShouldPassContext", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    CommandExecutionContext captured = null!;
                    engine.RegisterCommand("ctxTest", async (ctx, ct) =>
                    {
                        captured = ctx;
                        await Task.CompletedTask;
                    });
                    var context = new CommandExecutionContext { Name = "ctxTest" };
                    engine.ExecuteAsync("ctxTest", context).Wait();
                    return captured != null && captured.Name == "ctxTest";
                })
                .Add("Execute_CommandThatThrows_ShouldReturnFalse", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    engine.RegisterCommand("thrower", async (ctx, ct) =>
                    {
                        await Task.CompletedTask;
                        throw new InvalidOperationException("test error");
                    });
                    var success = engine.ExecuteAsync("thrower").Result;
                    return !success;
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_UndoRedo()
        {
            var result = new TestGroupBuilder("UndoRedo")
                .Add("Undo_WithUndoSupport_ShouldSucceed", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    var undone = false;
                    engine.RegisterCommand("undoable", async (ctx, ct) => { await Task.CompletedTask; },
                        async (ctx, ct) => { undone = true; await Task.CompletedTask; });
                    engine.ExecuteAsync("undoable").Wait();
                    var result2 = engine.UndoAsync().Result;
                    return result2 && undone;
                })
                .Add("Undo_WithoutUndoSupport_ShouldReturnFalse", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    engine.RegisterCommand("noUndo", async (ctx, ct) => { await Task.CompletedTask; });
                    engine.ExecuteAsync("noUndo").Wait();
                    var result2 = engine.UndoAsync().Result;
                    return !result2;
                })
                .Add("Undo_EmptyHistory_ShouldReturnFalse", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    var result2 = engine.UndoAsync().Result;
                    return !result2;
                })
                .Add("Redo_AfterUndo_ShouldSucceed", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    var reExecuted = false;
                    engine.RegisterCommand("redoable", async (ctx, ct) =>
                    {
                        reExecuted = true;
                        await Task.CompletedTask;
                    }, async (ctx, ct) => { await Task.CompletedTask; });
                    engine.ExecuteAsync("redoable").Wait();
                    engine.UndoAsync().Wait();
                    reExecuted = false;
                    var result2 = engine.RedoAsync().Result;
                    return result2 && reExecuted;
                })
                .Add("Redo_EmptyRedoStack_ShouldReturnFalse", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    var result2 = engine.RedoAsync().Result;
                    return !result2;
                })
                .Add("CanUndo_AfterExecute_ShouldBeTrue", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    engine.RegisterCommand("test", async (ctx, ct) => { await Task.CompletedTask; },
                        async (ctx, ct) => { await Task.CompletedTask; });
                    engine.ExecuteAsync("test").Wait();
                    return engine.CanUndo;
                })
                .Add("CanRedo_AfterUndo_ShouldBeTrue", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    engine.RegisterCommand("test", async (ctx, ct) => { await Task.CompletedTask; },
                        async (ctx, ct) => { await Task.CompletedTask; });
                    engine.ExecuteAsync("test").Wait();
                    engine.UndoAsync().Wait();
                    return engine.CanRedo;
                })
                .Add("ClearHistory_ShouldResetUndoRedo", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    engine.RegisterCommand("test", async (ctx, ct) => { await Task.CompletedTask; },
                        async (ctx, ct) => { await Task.CompletedTask; });
                    engine.ExecuteAsync("test").Wait();
                    engine.ClearHistory();
                    return !engine.CanUndo && !engine.CanRedo;
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_GetRegisteredCommands()
        {
            var result = new TestGroupBuilder("GetRegisteredCommands")
                .Add("GetRegisteredCommands_ShouldReturnAllNames", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    engine.RegisterCommand("cmd1", async (ctx, ct) => { await Task.CompletedTask; });
                    engine.RegisterCommand("cmd2", async (ctx, ct) => { await Task.CompletedTask; });
                    var names = engine.GetRegisteredCommands();
                    return names.Length == 2
                        && names.Contains("cmd1")
                        && names.Contains("cmd2");
                })
                .Add("GetRegisteredCommands_Empty_ShouldReturnEmpty", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    var names = engine.GetRegisteredCommands();
                    return names.Length == 0;
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_Extensions()
        {
            var result = new TestGroupBuilder("Extensions")
                .Add("RegisterHandler_StringFunc_ShouldWork", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    var handled = false;
                    engine.RegisterHandler("handler1", (string cmd) =>
                    {
                        handled = true;
                        return Task.FromResult(true);
                    });
                    var success = engine.ExecuteAsync("handler1").Result;
                    return success && handled;
                })
                .Add("RegisterHandler_SyncFunc_ShouldWork", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    var handled = false;
                    engine.RegisterHandler("handler2", (string cmd) =>
                    {
                        handled = true;
                        return true;
                    });
                    var success = engine.ExecuteAsync("handler2").Result;
                    return success && handled;
                })
                .Add("IsCommandRegistered_ShouldReturnCorrect", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    engine.RegisterCommand("exists", async (ctx, ct) => { await Task.CompletedTask; });
                    return engine.IsCommandRegistered("exists") && !engine.IsCommandRegistered("notExists");
                })
                .Add("RegisterHandlers_Batch_ShouldWork", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    var handlers = new Dictionary<string, Func<string, Task<bool>>>
                    {
                        ["batch1"] = cmd => Task.FromResult(true),
                        ["batch2"] = cmd => Task.FromResult(true)
                    };
                    engine.RegisterHandlers(handlers);
                    return engine.IsCommandRegistered("batch1")
                        && engine.IsCommandRegistered("batch2");
                })
                .Run();

            result.AssertAllPassed();
        }
    }
}
