#nullable enable


using BackEndDriven.Abstractions;
using BackEndDriven.Models;
using BackEndDriven.Pipeline;
using TestInfrastructure;

namespace BackEndDriven.Tests;

public class BackEndPipelineRunnerTests
{
    public void RunAll_PipelineWorkflowInvocation()
    {
        var result = new TestGroupBuilder("PipelineWorkflowInvocation")

            .Add("PipelineWorkflow_Success_ShouldReturnContextWithStageResult", () =>
            {
                var runner = new BackEndPipelineRunner<TestPipelineContext>();
                var context = new TestPipelineContext { Mode = "CLI" };

                var pipeline = new MockPipeline();

                var executed = runner.ExecutePipelineSafe(
                    createContext: () => context,
                    createPipeline: ctx => pipeline,
                    executePipeline: (ctx, pipelineObj) =>
                    {
                        if (pipelineObj is MockPipeline typedPipeline)
                        {
                            typedPipeline.Execute(ctx);
                            ctx.SetStageResult("ExecutePipeline", true, "OK");
                        }
                    },
                    onError: ex => { throw new InvalidOperationException("不应进入 onError", ex); });

                return executed == context
                       && executed.Mode == "CLI"
                       && executed.GetStageSuccess("ExecutePipeline") == true
                       && executed.GetStageError("ExecutePipeline") == "OK";
            })

            .Add("PipelineWorkflow_TypeMismatch_ShouldHandleGracefully", () =>
            {
                var runner = new BackEndPipelineRunner<TestPipelineContext>();
                var context = new TestPipelineContext { Mode = "CLI" };

                var wrongType = new object();

                var executed = runner.ExecutePipelineSafe(
                    createContext: () => context,
                    createPipeline: ctx => wrongType,
                    executePipeline: (ctx, pipelineObj) =>
                    {
                        if (pipelineObj is MockPipeline)
                        {
                            throw new InvalidOperationException("不应匹配 MockPipeline");
                        }
                        else
                        {
                            ctx.SetStageResult("ExecutePipeline", false,
                                $"Pipeline type mismatch: expected MockPipeline, got {pipelineObj.GetType().Name}");
                        }
                    },
                    onError: ex => { throw new InvalidOperationException("不应进入 onError", ex); });

                return executed == context
                       && executed.GetStageSuccess("ExecutePipeline") == false
                       && executed.GetStageError("ExecutePipeline")!.Contains("type mismatch");
            })

            .Add("PipelineWorkflow_ExecuteError_ShouldCallOnErrorWithFallback", () =>
            {
                var runner = new BackEndPipelineRunner<TestPipelineContext>();
                var context = new TestPipelineContext { Mode = "CLI", CommandFile = "/tmp/test.cmd" };
                Exception? capturedException = null;

                var executed = runner.ExecutePipelineSafe(
                    createContext: () => context,
                    createPipeline: ctx => new MockPipeline(),
                    executePipeline: (ctx, pipelineObj) =>
                    {
                        throw new InvalidOperationException("管线执行失败: 命令文件不存在");
                    },
                    onError: ex =>
                    {
                        capturedException = ex;
                        var errorContext = new TestPipelineContext
                        {
                            Mode = context.Mode,
                            CommandFile = context.CommandFile,
                            IsExecuting = false
                        };
                        errorContext.SetStageResult("ExecutePipeline", false,
                            $"ExecutePipeline failed: {ex.Message}");
                        return errorContext;
                    });

                return executed != context
                       && executed.Mode == "CLI"
                       && executed.CommandFile == "/tmp/test.cmd"
                       && executed.GetStageSuccess("ExecutePipeline") == false
                       && executed.GetStageError("ExecutePipeline")!.Contains("命令文件不存在")
                       && capturedException is InvalidOperationException
                       && capturedException.Message == "管线执行失败: 命令文件不存在";
            })

            .Add("PipelineWorkflow_CreateContextError_ShouldCallOnError", () =>
            {
                var runner = new BackEndPipelineRunner<TestPipelineContext>();
                Exception? capturedException = null;

                var executed = runner.ExecutePipelineSafe(
                    createContext: () => throw new FileNotFoundException("配置文件未找到", "config.json"),
                    createPipeline: ctx => { throw new InvalidOperationException("不应调用 createPipeline"); },
                    executePipeline: (ctx, pipelineObj) => { throw new InvalidOperationException("不应调用 executePipeline"); },
                    onError: ex =>
                    {
                        capturedException = ex;
                        return new TestPipelineContext
                        {
                            Mode = "Error",
                            CommandFile = ex is FileNotFoundException fnf ? fnf.FileName : "unknown"
                        };
                    });

                return executed.Mode == "Error"
                       && executed.CommandFile == "config.json"
                       && capturedException is FileNotFoundException fnf2
                       && fnf2.FileName == "config.json";
            })

            .Add("PipelineWorkflow_MultipleStages_ShouldCollectStageResults", () =>
            {
                var runner = new BackEndPipelineRunner<TestPipelineContext>();
                var context = new TestPipelineContext();

                var pipeline = new MockPipeline();
                pipeline.RegisterStage("Validate");
                pipeline.RegisterStage("Execute");
                pipeline.RegisterStage("Collect");

                runner.ExecutePipelineSafe(
                    createContext: () => context,
                    createPipeline: ctx => pipeline,
                    executePipeline: (ctx, pipelineObj) =>
                    {
                        if (pipelineObj is MockPipeline typedPipeline)
                        {
                            typedPipeline.Execute(ctx);
                        }
                    },
                    onError: ex => { throw new InvalidOperationException("不应进入 onError", ex); });

                return pipeline.ExecutedStages.Count == 3
                       && pipeline.ExecutedStages[0] == "Validate"
                       && pipeline.ExecutedStages[1] == "Execute"
                       && pipeline.ExecutedStages[2] == "Collect";
            })

            .Add("PipelineWorkflow_ErrorContext_ShouldContainFullErrorInfo", () =>
            {
                var runner = new BackEndPipelineRunner<TestPipelineContext>();
                var originalContext = new TestPipelineContext
                {
                    Mode = "GUI",
                    CommandFile = "cmds.txt",
                    BatchId = "batch-001"
                };

                var executed = runner.ExecutePipelineSafe(
                    createContext: () => originalContext,
                    createPipeline: ctx => new MockPipeline(),
                    executePipeline: (ctx, pipelineObj) =>
                    {
                        throw new TimeoutException("管线执行超时 (30000ms)");
                    },
                    onError: ex =>
                    {
                        var errorCtx = new TestPipelineContext
                        {
                            Mode = originalContext.Mode,
                            CommandFile = originalContext.CommandFile,
                            BatchId = originalContext.BatchId,
                            IsExecuting = false
                        };
                        errorCtx.SetStageResult("ExecutePipeline", false, ex.Message);
                        errorCtx.SetStageResult("ErrorDetail", false,
                            $"Type={ex.GetType().Name}, TimeoutMs=30000");
                        return errorCtx;
                    });

                return executed.Mode == "GUI"
                       && executed.CommandFile == "cmds.txt"
                       && executed.BatchId == "batch-001"
                       && executed.GetStageSuccess("ExecutePipeline") == false
                       && executed.GetStageError("ExecutePipeline") == "管线执行超时 (30000ms)"
                       && executed.GetStageError("ErrorDetail")!.Contains("Timeout");
            })

            .Add("PipelineWorkflow_NullPipeline_ShouldHandleGracefully", () =>
            {
                var runner = new BackEndPipelineRunner<TestPipelineContext>();
                var context = new TestPipelineContext();

                var executed = runner.ExecutePipelineSafe(
                    createContext: () => context,
                    createPipeline: ctx => null!,
                    executePipeline: (ctx, pipelineObj) =>
                    {
                        if (pipelineObj == null)
                        {
                            ctx.SetStageResult("ExecutePipeline", false, "Pipeline is null");
                        }
                        else
                        {
                            ctx.SetStageResult("ExecutePipeline", true, "OK");
                        }
                    },
                    onError: ex => { throw new InvalidOperationException("不应进入 onError", ex); });

                return executed.GetStageSuccess("ExecutePipeline") == false
                       && executed.GetStageError("ExecutePipeline") == "Pipeline is null";
            })

            .Run();

        result.AssertAllPassed();
    }

    public void RunAll_RecordCommandResult()
    {
        var result = new TestGroupBuilder("RecordCommandResult")
            .Add("RecordCommandResult_Success_ShouldUpdateCounts", () =>
            {
                IBackEndPipelineContext ctx = new TestPipelineContext();

                ctx.RecordCommandResult(new CommandLineResult
                {
                    CommandText = "echo hello",
                    Success = true,
                    Message = "OK"
                });

                return ctx.SuccessCount == 1
                       && ctx.FailCount == 0
                       && ctx.CommandResults.Count == 1;
            })

            .Add("RecordCommandResult_Fail_ShouldUpdateCounts", () =>
            {
                IBackEndPipelineContext ctx = new TestPipelineContext();

                ctx.RecordCommandResult(new CommandLineResult
                {
                    CommandText = "invalid_cmd",
                    Success = false,
                    Message = "Command not found"
                });

                return ctx.SuccessCount == 0
                       && ctx.FailCount == 1
                       && ctx.CommandResults.Count == 1;
            })

            .Add("RecordCommandResult_Multiple_ShouldAggregate", () =>
            {
                IBackEndPipelineContext ctx = new TestPipelineContext();

                ctx.RecordCommandResult(new CommandLineResult { Success = true, Message = "OK" });
                ctx.RecordCommandResult(new CommandLineResult { Success = false, Message = "Err1" });
                ctx.RecordCommandResult(new CommandLineResult { Success = true, Message = "OK2" });

                return ctx.SuccessCount == 2
                       && ctx.FailCount == 1
                       && ctx.CommandResults.Count == 3;
            })

            .Add("RecordCommandResult_Reset_ShouldClearAll", () =>
            {
                IBackEndPipelineContext ctx = new TestPipelineContext();

                ctx.RecordCommandResult(new CommandLineResult { Success = true });
                ctx.RecordCommandResult(new CommandLineResult { Success = true });
                ctx.Reset();

                return ctx.SuccessCount == 0
                       && ctx.FailCount == 0
                       && ctx.CommandResults.Count == 0;
            })

            .Add("GetSummary_ShouldReturnFormattedString", () =>
            {
                IBackEndPipelineContext ctx = new TestPipelineContext();

                ctx.RecordCommandResult(new CommandLineResult { Success = true });
                ctx.RecordCommandResult(new CommandLineResult { Success = false });

                var summary = ctx.GetSummary();
                return summary.Contains("Total=") && summary.Contains("Success=1") && summary.Contains("Fail=1");
            })

            .Add("RecordCommandResult_IBackEndPipelineContext_Cast_ShouldWork", () =>
            {
                var ctx = new TestPipelineContext();

                ((IBackEndPipelineContext)ctx).RecordCommandResult(new CommandLineResult
                {
                    CommandText = "test command",
                    Success = true,
                    Message = "OK"
                });

                return ctx.SuccessCount == 1
                       && ctx.FailCount == 0
                       && ctx.CommandResults[0].CommandText == "test command";
            })

            .Run();

        result.AssertAllPassed();
    }
}


public class MockPipeline
{
    public List<string> ExecutedStages { get; } = new();
    private readonly List<string> _stages = new();

    public void RegisterStage(string name) => _stages.Add(name);

    public void Execute(TestPipelineContext context)
    {
        foreach (var stage in _stages)
        {
            ExecutedStages.Add(stage);
            context.SetStageResult(stage, true, $"{stage} completed");
        }
    }
}


public class TestPipelineContext : IBackEndPipelineContext
{

    public int TotalCommands { get; set; }
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
    public int SkipCount { get; set; }
    public int CurrentIndex { get; set; } = -1;
    public List<CommandLineResult> CommandResults { get; set; } = new();


    public string? Mode { get; set; }
    public string? BatchId { get; set; }
    public string? ConfigPath { get; set; }
    public string? CommandFile { get; set; }
    public bool AutoClose { get; set; }


    public bool IsCancelled { get; set; }
    public bool IsExecuting { get; set; }


    public Dictionary<string, object?> SharedData { get; set; } = new(StringComparer.OrdinalIgnoreCase);


    private readonly Dictionary<string, (bool Success, string? Error)> _stageResults = new();

    public void SetStageResult(string stage, bool success, string? error = null)
    {
        _stageResults[stage] = (success, error);
    }

    public bool? GetStageSuccess(string stage)
        => _stageResults.TryGetValue(stage, out var r) ? r.Success : null;

    public string? GetStageError(string stage)
        => _stageResults.TryGetValue(stage, out var r) ? r.Error : null;


    void IBackEndPipelineContext.RecordCommandResult(CommandLineResult result)
    {
        CommandResults.Add(result);
        if (result.Success)
            SuccessCount++;
        else
            FailCount++;
    }

    void IBackEndPipelineContext.Reset()
    {
        TotalCommands = 0;
        SuccessCount = 0;
        FailCount = 0;
        SkipCount = 0;
        CommandResults.Clear();
        IsCancelled = false;
        IsExecuting = false;
        SharedData.Clear();
    }

    string IBackEndPipelineContext.GetSummary()
        => $"Total={TotalCommands}, Success={SuccessCount}, Fail={FailCount}, Skip={SkipCount}";
}