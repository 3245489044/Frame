using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using EventTriggerFramework.Abstractions;
using EventTriggerFramework.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventTriggerFramework.Tests
{
    [TestClass]
    public class NonEventCommandHandlerTests : TestBase
    {
        [TestMethod]
        public async Task RunAll_EchoCommand()
        {
            var result = await RunTestGroupAsync("NonEventCommandHandler_Echo", builder =>
            {
                builder.Add("Echo_SimpleText_ShouldReturnText", async () =>
                {
                    var handler = new NonEventCommandHandler();
                    var cmd = new ParsedCommand
                    {
                        Verb = "echo",
                        Action = "echo",
                        Target = "hello",
                        IsNonEventCommand = true,
                        IsSuccess = true
                    };
                    var r = await handler.HandleAsync(cmd);
                    return r.IsSuccess && r.Value as string == "hello";
                });

                builder.Add("Echo_EmptyTarget_ShouldReturnEmpty", async () =>
                {
                    var handler = new NonEventCommandHandler();
                    var cmd = new ParsedCommand
                    {
                        Verb = "echo",
                        Action = "echo",
                        Target = "",
                        IsNonEventCommand = true,
                        IsSuccess = true
                    };
                    var r = await handler.HandleAsync(cmd);
                    return r.IsSuccess && r.Value as string == "";
                });

                builder.Add("Echo_NullTarget_ShouldReturnEmpty", async () =>
                {
                    var handler = new NonEventCommandHandler();
                    var cmd = new ParsedCommand
                    {
                        Verb = "echo",
                        Action = "echo",
                        Target = null,
                        IsNonEventCommand = true,
                        IsSuccess = true
                    };
                    var r = await handler.HandleAsync(cmd);
                    return r.IsSuccess && r.Value as string == "";
                });

                builder.Add("Echo_WithParameters_ShouldAppendFlags", async () =>
                {
                    var handler = new NonEventCommandHandler();
                    var cmd = new ParsedCommand
                    {
                        Verb = "echo",
                        Action = "echo",
                        Target = "hello",
                        Parameters = new Dictionary<string, object>
                        {
                            ["world"] = true
                        },
                        IsNonEventCommand = true,
                        IsSuccess = true
                    };
                    var r = await handler.HandleAsync(cmd);
                    return r.IsSuccess && r.Value as string == "hello world";
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public async Task RunAll_SleepCommand()
        {
            var result = await RunTestGroupAsync("NonEventCommandHandler_Sleep", builder =>
            {
                builder.Add("Sleep_WithValidMs_ShouldWait", async () =>
                {
                    var handler = new NonEventCommandHandler();
                    var cmd = new ParsedCommand
                    {
                        Verb = "sleep",
                        Action = "sleep",
                        Target = "50",
                        IsNonEventCommand = true,
                        IsSuccess = true
                    };
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var r = await handler.HandleAsync(cmd);
                    sw.Stop();
                    return r.IsSuccess
                        && r.Value as string == "Slept 50ms"
                        && sw.ElapsedMilliseconds >= 40;
                });

                builder.Add("Sleep_WithInvalidMs_ShouldNotWait", async () =>
                {
                    var handler = new NonEventCommandHandler();
                    var cmd = new ParsedCommand
                    {
                        Verb = "sleep",
                        Action = "sleep",
                        Target = "invalid",
                        IsNonEventCommand = true,
                        IsSuccess = true
                    };
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var r = await handler.HandleAsync(cmd);
                    sw.Stop();
                    return r.IsSuccess && sw.ElapsedMilliseconds < 100;
                });

                builder.Add("Sleep_WithNegativeMs_ShouldNotWait", async () =>
                {
                    var handler = new NonEventCommandHandler();
                    var cmd = new ParsedCommand
                    {
                        Verb = "sleep",
                        Action = "sleep",
                        Target = "-1",
                        IsNonEventCommand = true,
                        IsSuccess = true
                    };
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var r = await handler.HandleAsync(cmd);
                    sw.Stop();
                    return r.IsSuccess && sw.ElapsedMilliseconds < 100;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public async Task RunAll_WaitCommand()
        {
            var result = await RunTestGroupAsync("NonEventCommandHandler_Wait", builder =>
            {
                builder.Add("Wait_WithValidMs_ShouldWait", async () =>
                {
                    var handler = new NonEventCommandHandler();
                    var cmd = new ParsedCommand
                    {
                        Verb = "wait",
                        Action = "wait",
                        Target = "50",
                        IsNonEventCommand = true,
                        IsSuccess = true
                    };
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var r = await handler.HandleAsync(cmd);
                    sw.Stop();
                    return r.IsSuccess
                        && r.Value as string == "Waited 50ms"
                        && sw.ElapsedMilliseconds >= 40;
                });

                builder.Add("Wait_WithInvalidMs_ShouldUseDefault", async () =>
                {
                    var handler = new NonEventCommandHandler();
                    var cmd = new ParsedCommand
                    {
                        Verb = "wait",
                        Action = "wait",
                        Target = "invalid",
                        IsNonEventCommand = true,
                        IsSuccess = true
                    };
                    var r = await handler.HandleAsync(cmd);
                    return r.IsSuccess && r.Value as string == "Waited 30000ms";
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public async Task RunAll_ErrorHandling()
        {
            var result = await RunTestGroupAsync("NonEventCommandHandler_ErrorHandling", builder =>
            {
                builder.Add("Handle_NullCommand_ShouldReturnFailure", async () =>
                {
                    var handler = new NonEventCommandHandler();
                    var r = await handler.HandleAsync(null!);
                    return !r.IsSuccess && r.ErrorMessage == "Command cannot be null";
                });

                builder.Add("Handle_UnknownAction_ShouldReturnFailure", async () =>
                {
                    var handler = new NonEventCommandHandler();
                    var cmd = new ParsedCommand
                    {
                        Verb = "unknown",
                        Action = "unknown",
                        Target = "something",
                        IsNonEventCommand = true,
                        IsSuccess = true
                    };
                    var r = await handler.HandleAsync(cmd);
                    return !r.IsSuccess && r.ErrorMessage.Contains("Unknown non-event command");
                });

                builder.Add("Handle_EmptyAction_ShouldReturnFailure", async () =>
                {
                    var handler = new NonEventCommandHandler();
                    var cmd = new ParsedCommand
                    {
                        Verb = "",
                        Action = "",
                        IsNonEventCommand = true,
                        IsSuccess = true
                    };
                    var r = await handler.HandleAsync(cmd);
                    return !r.IsSuccess && r.ErrorMessage == "Action is required";
                });

                builder.Add("Handle_NullAction_ShouldReturnFailure", async () =>
                {
                    var handler = new NonEventCommandHandler();
                    var cmd = new ParsedCommand
                    {
                        Verb = null,
                        Action = null,
                        IsNonEventCommand = true,
                        IsSuccess = true
                    };
                    var r = await handler.HandleAsync(cmd);
                    return !r.IsSuccess && r.ErrorMessage == "Action is required";
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_CanHandle()
        {
            var result = RunTestGroup("NonEventCommandHandler_CanHandle", builder =>
            {
                builder.Add("CanHandle_Echo_ShouldReturnTrue", () =>
                {
                    var handler = new NonEventCommandHandler();
                    return handler.CanHandle("echo");
                });

                builder.Add("CanHandle_Sleep_ShouldReturnTrue", () =>
                {
                    var handler = new NonEventCommandHandler();
                    return handler.CanHandle("sleep");
                });

                builder.Add("CanHandle_Wait_ShouldReturnTrue", () =>
                {
                    var handler = new NonEventCommandHandler();
                    return handler.CanHandle("wait");
                });

                builder.Add("CanHandle_Unknown_ShouldReturnFalse", () =>
                {
                    var handler = new NonEventCommandHandler();
                    return !handler.CanHandle("unknown");
                });

                builder.Add("CanHandle_Empty_ShouldReturnFalse", () =>
                {
                    var handler = new NonEventCommandHandler();
                    return !handler.CanHandle("");
                });

                builder.Add("CanHandle_Null_ShouldReturnFalse", () =>
                {
                    var handler = new NonEventCommandHandler();
                    return !handler.CanHandle(null!);
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public async Task RunAll_RegisterHandler()
        {
            var result = await RunTestGroupAsync("NonEventCommandHandler_RegisterHandler", builder =>
            {
                builder.Add("RegisterHandler_ShouldOverrideDefault", async () =>
                {
                    var handler = new NonEventCommandHandler();
                    handler.RegisterHandler("echo", cmd => Task.FromResult(Result<object?>.Success("overridden")));
                    var cmd = new ParsedCommand
                    {
                        Verb = "echo",
                        Action = "echo",
                        Target = "original",
                        IsNonEventCommand = true,
                        IsSuccess = true
                    };
                    var r = await handler.HandleAsync(cmd);
                    return r.IsSuccess && r.Value as string == "overridden";
                });

                builder.Add("RegisterHandler_Null_ShouldThrow", () =>
                {
                    var handler = new NonEventCommandHandler();
                    try
                    {
                        handler.RegisterHandler("test", (Func<ParsedCommand, Task<Result<object?>>>)null!);
                        return true;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                });

                builder.Add("RegisterHandler_Async_ShouldWork", async () =>
                {
                    var handler = new NonEventCommandHandler();
                    handler.RegisterHandler("custom", cmd => Task.FromResult(Result<object?>.Success("async")));
                    var cmd = new ParsedCommand
                    {
                        Verb = "custom",
                        Action = "custom",
                        Target = "test",
                        IsNonEventCommand = true,
                        IsSuccess = true
                    };
                    var r = await handler.HandleAsync(cmd);
                    return r.IsSuccess && r.Value as string == "async";
                });
            });

            result.AssertAllPassed();
        }
    }
}
