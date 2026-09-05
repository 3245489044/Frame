using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using EventTriggerFramework.Abstractions;
using EventTriggerFramework.Pipeline;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventTriggerFramework.Tests
{
    [TestClass]
    public class PipelineStageTests : TestBase
    {
        [TestMethod]
        public void RunAll_PipelineStageBase()
        {
            var result = RunTestGroup("PipelineStageBase", builder =>
            {
                builder.Add("ExecuteAsync_ShouldInvokeExecuteCore", async () =>
                {
                    bool executed = false;
                    var stage = new TestStage("test", async (ctx, ct) =>
                    {
                        executed = true;
                        ctx.SetStageResult("test", true);
                        await Task.CompletedTask;
                    });
                    var ctx = new TestContext();
                    await stage.ExecuteAsync(ctx);
                    return executed;
                });

                builder.Add("Name_ShouldMatch", () =>
                {
                    var stage = new TestStage("myStage", async (ctx, ct) =>
                    {
                        ctx.SetStageResult("myStage", true);
                        await Task.CompletedTask;
                    });
                    return stage.Name == "myStage";
                });

                builder.Add("Description_Default_ShouldNotBeEmpty", () =>
                {
                    var stage = new TestStage("test", async (ctx, ct) =>
                    {
                        ctx.SetStageResult("test", true);
                        await Task.CompletedTask;
                    });
                    return !string.IsNullOrEmpty(stage.Description);
                });

                builder.Add("CanExecute_Default_ShouldReturnTrue", () =>
                {
                    var stage = new TestStage("test", async (ctx, ct) =>
                    {
                        ctx.SetStageResult("test", true);
                        await Task.CompletedTask;
                    });
                    return stage.CanExecute(new TestContext());
                });

                builder.Add("ExecuteAsync_Exception_ShouldSetStageResultFailure", async () =>
                {
                    var stage = new TestStage("faulty", async (ctx, ct) =>
                    {
                        throw new InvalidOperationException("oops");
                    });
                    var ctx = new TestContext();
                    await stage.ExecuteAsync(ctx);
                    var result = ctx.GetStageResult("faulty");
                    return result != null && !result.Value.IsSuccess;
                });

                builder.Add("ExecuteAsync_Cancellation_ShouldThrow", async () =>
                {
                    var stage = new TestStage("cancel", async (ctx, ct) =>
                    {
                        await Task.Delay(10000, ct);
                    });
                    var ctx = new TestContext();
                    using var cts = new CancellationTokenSource();
                    cts.Cancel();
                    try
                    {
                        await stage.ExecuteAsync(ctx, cts.Token);
                        return false;
                    }
                    catch (OperationCanceledException)
                    {
                        return true;
                    }
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_CommandStageBase()
        {
            var result = RunTestGroup("CommandStageBase", builder =>
            {
                builder.Add("IsEventCommand_NonEventCommand_ShouldReturnFalse", () =>
                {
                    var ctx = new CommandContext();
                    ctx.ParsedCommand = new ParsedCommand
                    {
                        IsNonEventCommand = true,
                        NormalizedCommand = "echo hello"
                    };
                    return !CommandStageBaseHelper.IsEventCommand(ctx);
                });

                builder.Add("IsEventCommand_NullNormalized_ShouldReturnFalse", () =>
                {
                    var ctx = new CommandContext();
                    ctx.ParsedCommand = new ParsedCommand
                    {
                        IsNonEventCommand = false,
                        NormalizedCommand = null
                    };
                    return !CommandStageBaseHelper.IsEventCommand(ctx);
                });

                builder.Add("IsEventCommand_ValidEvent_ShouldReturnTrue", () =>
                {
                    var ctx = new CommandContext();
                    ctx.ParsedCommand = new ParsedCommand
                    {
                        IsNonEventCommand = false,
                        NormalizedCommand = "do click btnOK"
                    };
                    return CommandStageBaseHelper.IsEventCommand(ctx);
                });

                builder.Add("ResolveDependency_Injected_ShouldReturnInjected", () =>
                {
                    var ctx = new CommandContext();
                    var injected = "injected";
                    var resolved = CommandStageBaseHelper.ResolveDependency(ctx, injected, _ => null, "key");
                    return resolved == "injected";
                });

                builder.Add("ResolveDependency_Context_ShouldReturnFromContext", () =>
                {
                    var ctx = new CommandContext();
                    ctx.SharedData["key"] = "fromContext";
                    var resolved = CommandStageBaseHelper.ResolveDependency<string>(ctx, null, _ => "fromContext", "key");
                    return resolved == "fromContext";
                });

                builder.Add("ResolveDependency_SharedData_ShouldReturnFromShared", () =>
                {
                    var ctx = new CommandContext();
                    ctx.SharedData["myKey"] = "fromShared";
                    var resolved = CommandStageBaseHelper.ResolveDependency<string>(ctx, null, null, "myKey");
                    return resolved == "fromShared";
                });

                builder.Add("ResolveDependency_NotFound_ShouldReturnNull", () =>
                {
                    var ctx = new CommandContext();
                    var resolved = CommandStageBaseHelper.ResolveDependency<string>(ctx, null, null, "nonExistent");
                    return resolved == null;
                });
            });

            result.AssertAllPassed();
        }
    }

    public class TestStage : PipelineStageBase<TestContext>
    {
        private readonly Func<TestContext, CancellationToken, Task> _executeCore;

        public TestStage(string name, Func<TestContext, CancellationToken, Task> executeCore, string? description = null)
            : base(name, description)
        {
            _executeCore = executeCore;
        }

        protected override async Task ExecuteCoreAsync(TestContext context, CancellationToken cancellationToken)
        {
            await _executeCore(context, cancellationToken);
        }
    }

    public class TestContext : BasicMethod.Pipeline.IPipelineContext
    {
        private readonly Dictionary<string, (bool IsSuccess, string? Message)> _stageResults = new();
        private readonly Dictionary<string, object?> _sharedData = new();

        public string Mode { get; set; } = "Test";
        public IReadOnlyDictionary<string, bool> StageSuccess =>
            _stageResults.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.IsSuccess);
        public IReadOnlyDictionary<string, string?> StageErrors =>
            _stageResults.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Message);
        public IDictionary<string, object?> SharedData => _sharedData;
        public bool HasError => _stageResults.Values.Any(r => !r.IsSuccess);
        public string? FirstError => _stageResults.Values.FirstOrDefault(r => !r.IsSuccess).Message;

        public void SetStageResult(string stageName, bool isSuccess, string? message = null)
        {
            _stageResults[stageName] = (isSuccess, message);
        }

        public bool GetStageSuccess(string stageName)
        {
            return _stageResults.TryGetValue(stageName, out var result) && result.IsSuccess;
        }

        public string? GetStageError(string stageName)
        {
            return _stageResults.TryGetValue(stageName, out var result) ? result.Message : null;
        }

        public (bool IsSuccess, string? Message)? GetStageResult(string stageName)
        {
            return _stageResults.TryGetValue(stageName, out var result) ? result : null;
        }

        public void Reset()
        {
            _stageResults.Clear();
            _sharedData.Clear();
        }
    }

    public static class CommandStageBaseHelper
    {
        public static bool IsEventCommand(CommandContext context)
        {
            if (context.ParsedCommand?.IsNonEventCommand == true)
                return false;
            if (context.ParsedCommand?.NormalizedCommand == null)
                return false;
            return true;
        }

        public static T? ResolveDependency<T>(CommandContext context, T? injected,
            Func<CommandContext, T?>? contextSelector = null,
            string? sharedDataKey = null) where T : class
        {
            if (injected != null)
                return injected;
            if (contextSelector != null)
            {
                var fromContext = contextSelector(context);
                if (fromContext != null)
                    return fromContext;
            }
            if (sharedDataKey != null &&
                context.SharedData.TryGetValue(sharedDataKey, out var obj) &&
                obj is T typed)
            {
                return typed;
            }
            return null;
        }
    }
}
