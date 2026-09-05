using BasicExecutionResult = TConfig.ExecutionResult;

namespace WorkflowExecution.Abstractions;

public interface IPipelineStage
{
    string Name { get; }

    float Order { get; }

    Task<BasicExecutionResult> ExecuteAsync(
        TConfig.IExecutionContext context,
        CancellationToken cancellationToken = default);
}

public static class PipelineStageNames
{
    public const string Initialization = "Initialization";

    public const string PreProcessing = "PreProcessing";

    public const string Execution = "Execution";

    public const string PostProcessing = "PostProcessing";

    public const string Finalization = "Finalization";

    public static readonly IReadOnlyList<string> AllStages = new List<string>
    {
        Initialization,
        PreProcessing,
        Execution,
        PostProcessing,
        Finalization
    }.AsReadOnly();
}


