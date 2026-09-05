
using CommandFramework.Models;

namespace CommandFramework.Pipeline.Stages;

public class ResultCollectStage : CommandStageBase
{
    public ResultCollectStage()
        : base("ResultCollect", "Collect execution results from all stages")
    { }

    protected override Task ExecuteCoreAsync(CommandContext context, CancellationToken ct)
    {
        var report = new CommandExecutionReport
        {
            Command = context.CurrentCommand,
            LineNumber = context.CurrentCommandLineNumber,
            IsNonEvent = context.ParsedCommand?.IsNonEventCommand ?? false,
            Stages = GetStageResults(context),
            TotalDuration = CalculateTotalDuration(context),
            IsSuccess = !context.HasError,
            ErrorMessage = context.GetErrorMessage()
        };

        context.ExecutionReport = report;
        context.SetStageResult(Name, true);
        return Task.CompletedTask;
    }

    private static List<StageResult> GetStageResults(CommandContext context)
    {
        var results = new List<StageResult>();

        var stageNames = GetExecutedStageNames(context);
        foreach (var stageName in stageNames)
        {
            var success = context.GetStageSuccess(stageName);
            var error = context.GetStageError(stageName);
            results.Add(new StageResult
            {
                StageName = stageName,
                IsSuccess = success,
                ErrorMessage = error,
                Duration = TimeSpan.Zero
            });
        }

        return results;
    }

    private static List<string> GetExecutedStageNames(CommandContext context)
    {
        return context.GetApplicableStages().ToList();
    }

    private static TimeSpan CalculateTotalDuration(CommandContext context)
    {
        return context.FunctionExecutionTime > TimeSpan.Zero
            ? context.FunctionExecutionTime
            : TimeSpan.Zero;
    }
}
