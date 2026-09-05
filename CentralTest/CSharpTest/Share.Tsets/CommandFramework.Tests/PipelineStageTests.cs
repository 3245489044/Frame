using TestInfrastructure;
using CommandFramework.Abstractions;
using CommandFramework.Execution;
using CommandFramework.Models;
using CommandFramework.Parsing;
using CommandFramework.Pipeline;
using CommandFramework.Pipeline.Stages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommandFramework.Tests
{
    [TestClass]
    public class PipelineStageTests : TestBase
    {
        [TestMethod]
        public void RunAll_ParseCommandStage()
        {
            var result = new TestGroupBuilder("ParseCommandStage")
                .Add("ParseCommandStage_DoClick_ShouldParse", () =>
                {
                    var parser = new CommandParser();
                    var stage = new ParseCommandStage(parser);
                    var ctx = new CommandContext { CurrentCommand = "do click addButton" };
                    stage.ExecuteAsync(ctx).Wait();
                    return ctx.ParsedCommand != null
                        && ctx.ParsedCommand.IsSuccess
                        && ctx.ParsedCommand.Verb == "do"
                        && ctx.ParsedCommand.Action == "click";
                })
                .Add("ParseCommandStage_Echo_ShouldRouteToNonEvent", () =>
                {
                    var parser = new CommandParser();
                    var stage = new ParseCommandStage(parser);
                    var ctx = new CommandContext { CurrentCommand = "echo test" };
                    stage.ExecuteAsync(ctx).Wait();
                    return ctx.ParsedCommand != null
                        && ctx.ParsedCommand.IsNonEventCommand;
                })
                .Add("ParseCommandStage_EmptyCommand_ShouldNotThrow", () =>
                {
                    var parser = new CommandParser();
                    var stage = new ParseCommandStage(parser);
                    var ctx = new CommandContext { CurrentCommand = "" };
                    stage.ExecuteAsync(ctx).Wait();
                    return ctx.ParsedCommand != null
                        && !ctx.ParsedCommand.IsSuccess;
                })
                .Add("ParseCommandStage_WithEngine_ShouldRouteEngineCommand", () =>
                {
                    var engine = CommandEngine.CreateDefault();
                    engine.RegisterCommand("myAction", async (ctx, ct) =>
                    {
                        ctx.Data = "executed";
                        await Task.CompletedTask;
                    });
                    var parser = new CommandParser();
                    var stage = new ParseCommandStage(parser);
                    var ctx = new CommandContext { CurrentCommand = "do myAction" };
                    stage.ExecuteAsync(ctx).Wait();
                    return ctx.ParsedCommand != null
                        && ctx.ParsedCommand.IsSuccess;
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_PipelineEntry()
        {
            var result = new TestGroupBuilder("PipelineEntry")
                .Add("CreateDefaultPipeline_ShouldNotThrow", () =>
                {
                    var pipeline = PipelineEntry.CreateDefaultPipeline();
                    return pipeline != null;
                })
                .Add("CreateDefaultPipeline_ShouldHaveStages", () =>
                {
                    var pipeline = PipelineEntry.CreateDefaultPipeline();
                    return pipeline != null;
                })
                .Add("CreateCliPipeline_ShouldNotThrow", () =>
                {
                    var pipeline = PipelineEntry.CreateCliPipeline();
                    return pipeline != null;
                })
                .Add("CreateCliPipeline_ShouldHaveParseStage", () =>
                {
                    var pipeline = PipelineEntry.CreateCliPipeline();
                    return pipeline != null;
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_PipelineEntryInstance()
        {
            var result = new TestGroupBuilder("PipelineEntryInstance")
                .Add("PipelineEntryInstance_Create_ShouldNotThrow", () =>
                {
                    var parser = new CommandParser();
                    using var instance = new PipelineEntryInstance(parser);
                    return instance != null;
                })
                .Add("PipelineEntryInstance_RunCommand_ShouldSucceed", () =>
                {
                    var parser = new CommandParser();
                    using var instance = new PipelineEntryInstance(parser);
                    var ctx = instance.RunCommandAsync("echo hello").Result;
                    return ctx != null;
                })
                .Add("PipelineEntryInstance_History_ShouldTrack", () =>
                {
                    var parser = new CommandParser();
                    using var instance = new PipelineEntryInstance(parser);
                    instance.RunCommandAsync("echo first").Wait();
                    instance.RunCommandAsync("echo second").Wait();
                    return instance.HistoryCount == 2;
                })
                .Add("PipelineEntryInstance_UndoRedo_ShouldNavigate", () =>
                {
                    var parser = new CommandParser();
                    using var instance = new PipelineEntryInstance(parser);
                    instance.RunCommandAsync("echo first").Wait();
                    instance.RunCommandAsync("echo second").Wait();
                    var prev = instance.Undo();
                    var next = instance.Redo();
                    return prev != null && next != null;
                })
                .Add("PipelineEntryInstance_ClearHistory_ShouldReset", () =>
                {
                    var parser = new CommandParser();
                    using var instance = new PipelineEntryInstance(parser);
                    instance.RunCommandAsync("echo test").Wait();
                    instance.ClearHistory();
                    return instance.HistoryCount == 0 && !instance.CanUndo;
                })
                .Run();

            result.AssertAllPassed();
        }
    }
}
